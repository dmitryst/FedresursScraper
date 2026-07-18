using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FedresursScraper.Services.Enrichments;

namespace FedresursScraper.Services;

public interface IRadEnrichmentService
{
    Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct);

    Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct);
}

/// <summary>
/// Обогащение лотов РАД. HTML через HttpClient (AngleSharp/HAP), картинки — HttpClient.
/// Матчинг: Bidding.TradeNumber ↔ «Идентификатор лота в ЕФРСБ» + Lot.LotNumber.
/// </summary>
public class RadEnrichmentService : IRadEnrichmentService
{
    private readonly LotsDbContext _context;
    private readonly ILotsFileStorageService _fileStorage;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RadEnrichmentService> _logger;
    private readonly IOptionsMonitor<RadEnrichmentOptions> _options;

    private const int BatchSize = 5;
    private const int MaxRetryCount = 3;
    public const string PlatformMarker = "Российский аукционный дом";

    public RadEnrichmentService(
        LotsDbContext context,
        ILotsFileStorageService fileStorage,
        HttpClient httpClient,
        ILogger<RadEnrichmentService> logger,
        IOptionsMonitor<RadEnrichmentOptions> options)
    {
        _context = context;
        _fileStorage = fileStorage;
        _httpClient = httpClient;
        _logger = logger;
        _options = options;
    }

    public async Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct)
    {
        var dateThreshold = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = _options.CurrentValue;
        var maxNoLinkAttempts = options.GetEffectiveMaxNoLinkWaitAttempts();
        var noLinkRecheckAfter = DateTime.UtcNow.AddHours(-options.GetEffectiveNoLinkRecheckIntervalHours());

        var biddings = await _context.Biddings
            .Include(b => b.Lots)
                .ThenInclude(l => l.Images)
            .Include(b => b.Lots)
                .ThenInclude(l => l.Documents)
            .Include(b => b.Lots)
                .ThenInclude(l => l.PriceSchedules)
            .Include(b => b.EnrichmentState)
            .Where(b => b.Platform.Contains(PlatformMarker))
            .Where(b => !b.IsEnriched ?? true)
            .Where(b => b.EnrichmentState == null || b.EnrichmentState.RetryCount < MaxRetryCount)
            .Where(b =>
                b.EnrichmentState == null
                || b.EnrichmentState.RetryCount > 0
                || (b.EnrichmentState.MissingImagesAttemptCount < maxNoLinkAttempts
                    && (b.EnrichmentState.LastAttemptAt == null
                        || b.EnrichmentState.LastAttemptAt <= noLinkRecheckAfter)))
            .Where(b => b.CreatedAt > dateThreshold)
            .OrderByDescending(b => b.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (!biddings.Any())
            return false;

        foreach (var bidding in biddings)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var result = await EnrichBiddingAsync(bidding, forceReenrich: false, ct);
                ApplyBiddingEnrichmentStatus(bidding, result, options);
                await _context.SaveChangesAsync(ct);
                LogBiddingEnrichmentSummary(bidding, result);
            }
            catch (Exception ex)
            {
                HandleError(bidding, ex);
                await _context.SaveChangesAsync(ct);
                _logger.LogError(
                    ex,
                    "Ошибка при обогащении торгов РАД {TradeNumber} (PublicIds={PublicIds})",
                    bidding.TradeNumber,
                    FormatLotPublicIds(bidding));
            }

            var delayMs = options.GetActionDelayMs();
            _logger.LogDebug("Пауза между торгами РАД: {DelayMs} мс", delayMs);
            await Task.Delay(delayMs, ct);
        }

        return true;
    }

    public async Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct)
    {
        var options = _options.CurrentValue;

        var bidding = await _context.Biddings
            .Include(b => b.Lots)
                .ThenInclude(l => l.Images)
            .Include(b => b.Lots)
                .ThenInclude(l => l.Documents)
            .Include(b => b.Lots)
                .ThenInclude(l => l.PriceSchedules)
            .Include(b => b.EnrichmentState)
            .FirstOrDefaultAsync(b => b.TradeNumber == tradeNumber || b.TradeNumber.StartsWith(tradeNumber), ct);

        if (bidding == null)
            throw new KeyNotFoundException($"Торги с номером {tradeNumber} не найдены в базе.");

        var result = await EnrichBiddingAsync(bidding, forceReenrich: true, ct);
        ApplyBiddingEnrichmentStatus(bidding, result, options);

        if (bidding.EnrichmentState != null && bidding.IsEnriched == true)
            bidding.EnrichmentState.LastError = null;

        await _context.SaveChangesAsync(ct);
        LogBiddingEnrichmentSummary(bidding, result, manual: true);
    }

    private void LogBiddingEnrichmentSummary(
        Bidding bidding,
        EnrichBiddingResult result,
        bool manual = false)
    {
        var prefix = manual ? "Ручное обогащение РАД" : "РАД";

        if (bidding.Lots.Count == 0)
        {
            _logger.LogWarning(
                "{Prefix} {TradeNumber}: в БД нет лотов (Lots=0, HasNoLots={HasNoLots}) — нечего обогащать, IsEnriched={BiddingEnriched}",
                prefix,
                bidding.TradeNumber,
                bidding.HasNoLots,
                bidding.IsEnriched == true);
            return;
        }

        _logger.LogInformation(
            "{Prefix} {TradeNumber}: лотов в БД {LotsCount}, PublicIds=[{PublicIds}], обогащено {Enriched}, без связки {NoLink}, уже было {Already}, торги IsEnriched={BiddingEnriched}",
            prefix,
            bidding.TradeNumber,
            bidding.Lots.Count,
            FormatLotPublicIds(bidding),
            result.EnrichedLots,
            result.SkippedNoLink,
            result.AlreadyEnriched,
            bidding.IsEnriched == true);
    }

    private static string FormatLotPublicIds(Bidding bidding) =>
        bidding.Lots.Count == 0
            ? "-"
            : string.Join(",", bidding.Lots.Select(l => l.PublicId));

    private async Task<EnrichBiddingResult> EnrichBiddingAsync(
        Bidding bidding,
        bool forceReenrich,
        CancellationToken ct)
    {
        var efrsbNorm = RadHtmlParser.NormalizeEfrsbLotId(bidding.TradeNumber);
        var options = _options.CurrentValue;
        var result = new EnrichBiddingResult();

        foreach (var lot in bidding.Lots)
        {
            if (!forceReenrich && lot.IsEnriched == true)
            {
                result.AlreadyEnriched++;
                continue;
            }

            var link = await FindLotLinkAsync(efrsbNorm, lot.LotNumber, ct);
            if (link == null)
            {
                result.SkippedNoLink++;
                _logger.LogInformation(
                    "РАД: нет связки для ЕФРСБ {TradeNumber}, лот {LotNumber} (PublicId={PublicId}). Ждём индексацию каталога.",
                    bidding.TradeNumber,
                    lot.LotNumber,
                    lot.PublicId);
                continue;
            }

            lot.Images.Clear();
            lot.Documents.Clear();
            lot.PriceSchedules.Clear();

            await Task.Delay(options.GetActionDelayMs(), ct);

            string html;
            try
            {
                html = await _httpClient.GetStringAsync(link.LotUrl, ct);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Не удалось загрузить страницу РАД {link.LotUrl}: {ex.Message}",
                    ex);
            }

            await ProcessImagesAsync(lot, html, ct);

            if (IsPublicOffer(bidding.Type))
                ProcessPriceSchedule(lot, html);

            lot.IsEnriched = true;
            lot.EnrichedAt = DateTime.UtcNow;
            result.EnrichedLots++;

            _logger.LogInformation(
                "РАД: лот {LotNumber} (PublicId={PublicId}) обогащён. Фото={Images}, этапов графика={Schedules}",
                lot.LotNumber,
                lot.PublicId,
                lot.Images.Count,
                lot.PriceSchedules.Count);
        }

        return result;
    }

    private void ApplyBiddingEnrichmentStatus(
        Bidding bidding,
        EnrichBiddingResult result,
        RadEnrichmentOptions options)
    {
        var allLotsEnriched = bidding.Lots.Count > 0 && bidding.Lots.All(l => l.IsEnriched == true);
        if (allLotsEnriched)
        {
            bidding.IsEnriched = true;
            bidding.EnrichedAt = DateTime.UtcNow;

            if (bidding.EnrichmentState != null)
            {
                bidding.EnrichmentState.LastError = null;
                bidding.EnrichmentState.MissingImagesAttemptCount = 0;
            }

            return;
        }

        if (result.SkippedNoLink > 0)
        {
            if (bidding.EnrichmentState == null)
            {
                bidding.EnrichmentState = new EnrichmentState
                {
                    BiddingId = bidding.Id,
                    RetryCount = 0,
                    MissingImagesAttemptCount = 1,
                    LastAttemptAt = DateTime.UtcNow,
                    LastError = $"Ожидание связки в каталоге РАД ({result.SkippedNoLink} лот(ов))."
                };
            }
            else
            {
                bidding.EnrichmentState.MissingImagesAttemptCount++;
                bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
                bidding.EnrichmentState.LastError =
                    $"Ожидание связки в каталоге РАД ({result.SkippedNoLink} лот(ов)). " +
                    $"Попытка {bidding.EnrichmentState.MissingImagesAttemptCount}/{options.GetEffectiveMaxNoLinkWaitAttempts()}.";
            }

            var maxAttempts = options.GetEffectiveMaxNoLinkWaitAttempts();
            if (bidding.EnrichmentState.MissingImagesAttemptCount >= maxAttempts)
            {
                bidding.IsEnriched = true;
                bidding.EnrichedAt = DateTime.UtcNow;

                _logger.LogWarning(
                    "РАД {TradeNumber} (PublicIds={PublicIds}): прекращаем ожидание связки после {Attempts} попыток. " +
                    "Необогащённых лотов: {NoLink}.",
                    bidding.TradeNumber,
                    FormatLotPublicIds(bidding),
                    bidding.EnrichmentState.MissingImagesAttemptCount,
                    result.SkippedNoLink);
            }
            else
            {
                bidding.IsEnriched = false;
            }

            return;
        }

        bidding.IsEnriched = false;
    }

    private async Task<RadLotLink?> FindLotLinkAsync(string efrsbNorm, string? lotNumber, CancellationToken ct)
    {
        var lotNorm = RadHtmlParser.NormalizeLotNumber(lotNumber);

        var link = await _context.RadLotLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.EfrsbLotIdNormalized == efrsbNorm && x.LotNumberNormalized == lotNorm,
                ct);

        if (link != null)
            return link;

        if (string.IsNullOrWhiteSpace(lotNumber) || lotNorm is "1" or "0")
        {
            var links = await _context.RadLotLinks
                .AsNoTracking()
                .Where(x => x.EfrsbLotIdNormalized == efrsbNorm)
                .OrderBy(x => x.LotNumberNormalized)
                .Take(2)
                .ToListAsync(ct);

            if (links.Count == 1)
                return links[0];
        }

        return null;
    }

    private async Task ProcessImagesAsync(Lot lot, string html, CancellationToken ct)
    {
        var imageUrls = RadHtmlParser.ExtractImageUrls(html);
        if (!imageUrls.Any())
        {
            _logger.LogInformation("Lot {LotNumber}: картинки на РАД не найдены.", lot.LotNumber);
            return;
        }

        int order = 0;
        foreach (var imgUrl in imageUrls)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await Task.Delay(_options.CurrentValue.GetImageDelayMs(), ct);

                var imgBytes = await _httpClient.GetByteArrayAsync(imgUrl, ct);
                if (imgBytes.Length == 0)
                    continue;

                var extension = GuessExtension(imgUrl);
                var fileName = $"lots/{lot.Id}/{Guid.NewGuid()}{extension}";
                var s3Url = await _fileStorage.UploadAsync(imgBytes, fileName);

                lot.Images.Add(new LotImage
                {
                    LotId = lot.Id,
                    Url = s3Url,
                    Order = order++
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Не удалось скачать картинку РАД {Url}: {Message}", imgUrl, ex.Message);
            }
        }
    }

    private void ProcessPriceSchedule(Lot lot, string html)
    {
        var rows = RadHtmlParser.ExtractPriceSchedule(html);
        if (rows.Count == 0)
        {
            _logger.LogInformation("Lot {LotNumber}: график снижения цены на РАД не найден.", lot.LotNumber);
            return;
        }

        foreach (var row in rows)
        {
            lot.PriceSchedules.Add(new LotPriceSchedule
            {
                LotId = lot.Id,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                Price = row.Price,
                Deposit = row.Deposit
            });
        }

        _logger.LogInformation(
            "Lot {LotNumber}: сохранено {Count} этапов снижения цены с РАД.",
            lot.LotNumber,
            lot.PriceSchedules.Count);
    }

    private static bool IsPublicOffer(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        return type.Contains("Публичное", StringComparison.OrdinalIgnoreCase)
               || type.Contains("публичного предложения", StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessExtension(string url)
    {
        var path = url.Split('?', '#')[0];
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
            return ".jpg";

        return ext.ToLowerInvariant();
    }

    private static void HandleError(Bidding bidding, Exception ex)
    {
        if (bidding.EnrichmentState == null)
        {
            bidding.EnrichmentState = new EnrichmentState
            {
                BiddingId = bidding.Id,
                RetryCount = 1,
                LastAttemptAt = DateTime.UtcNow,
                LastError = ex.Message
            };
        }
        else
        {
            bidding.EnrichmentState.RetryCount++;
            bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
            bidding.EnrichmentState.LastError = ex.Message;
        }
    }

    private sealed class EnrichBiddingResult
    {
        public int EnrichedLots { get; set; }
        public int SkippedNoLink { get; set; }
        public int AlreadyEnriched { get; set; }
    }
}
