using System.Net;
using System.Net.Http.Headers;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FedresursScraper.Services.Enrichments;
using OpenQA.Selenium;

namespace FedresursScraper.Services;

public interface IAlfalotEnrichmentService
{
    Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct);

    Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct);
}

/// <summary>
/// Обогащение лотов Альфалот. HTML через Selenium (InProtect), картинки — HttpClient с cookies браузера.
/// Статус ведётся на уровне лота (Lot.IsEnriched); торги закрываются, когда обогащены все лоты.
/// Нет связки в каталоге — не ошибка и не ретрай, просто ждём индексацию.
/// </summary>
public class AlfalotEnrichmentService : IAlfalotEnrichmentService
{
    private readonly LotsDbContext _context;
    private readonly ILotsFileStorageService _fileStorage;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly ILogger<AlfalotEnrichmentService> _logger;
    private readonly IOptionsMonitor<AlfalotEnrichmentOptions> _options;

    private const int BatchSize = 5;
    private const int MaxRetryCount = 3;
    public const string PlatformMarker = "Альфалот";

    public AlfalotEnrichmentService(
        LotsDbContext context,
        ILotsFileStorageService fileStorage,
        IWebDriverFactory webDriverFactory,
        ILogger<AlfalotEnrichmentService> logger,
        IOptionsMonitor<AlfalotEnrichmentOptions> options)
    {
        _context = context;
        _fileStorage = fileStorage;
        _webDriverFactory = webDriverFactory;
        _logger = logger;
        _options = options;
    }

    public async Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct)
    {
        var dateThreshold = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = _options.CurrentValue;
        var pageTimeout = options.GetPageLoadTimeout();
        var wafTimeout = options.GetWafWaitTimeout();
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
            // Для ожидания связки: не чаще чем раз в NoLinkRecheckIntervalHours,
            // и не больше MaxNoLinkWaitAttempts (счётчик = MissingImagesAttemptCount).
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

        IWebDriver? driver = null;
        HttpClient? httpClient = null;

        try
        {
            (driver, httpClient) = CreateBrowserSession(pageTimeout, wafTimeout);

            foreach (var bidding in biddings)
            {
                if (ct.IsCancellationRequested)
                    break;

                try
                {
                    var result = await EnrichBiddingAsync(
                        bidding,
                        driver,
                        httpClient,
                        wafTimeout,
                        forceReenrich: false,
                        ct);

                    ApplyBiddingEnrichmentStatus(bidding, result, options);

                    await _context.SaveChangesAsync(ct);

                    LogBiddingEnrichmentSummary(bidding, result, afterSessionRecreate: false);
                }
                catch (Exception ex) when (IsDeadBrowserSession(ex))
                {
                    // Не жжём RetryCount: это падение Chrome, а не ошибка страницы лота.
                    _logger.LogWarning(
                        ex,
                        "Chrome-сессия Альфалот умерла на торгах {TradeNumber} (PublicIds={PublicIds}). Пересоздаём браузер и повторяем.",
                        bidding.TradeNumber,
                        FormatLotPublicIds(bidding));

                    DisposeBrowserSession(ref driver, ref httpClient);

                    try
                    {
                        (driver, httpClient) = CreateBrowserSession(pageTimeout, wafTimeout);

                        var result = await EnrichBiddingAsync(
                            bidding,
                            driver,
                            httpClient,
                            wafTimeout,
                            forceReenrich: false,
                            ct);

                        ApplyBiddingEnrichmentStatus(bidding, result, options);
                        await _context.SaveChangesAsync(ct);

                        LogBiddingEnrichmentSummary(bidding, result, afterSessionRecreate: true);
                    }
                    catch (Exception retryEx)
                    {
                        if (IsDeadBrowserSession(retryEx))
                        {
                            _logger.LogError(
                                retryEx,
                                "Chrome снова упал на {TradeNumber} (PublicIds={PublicIds}) — прерываем пачку, чтобы не крутить мёртвую сессию.",
                                bidding.TradeNumber,
                                FormatLotPublicIds(bidding));
                            DisposeBrowserSession(ref driver, ref httpClient);
                            break;
                        }

                        HandleError(bidding, retryEx);
                        await _context.SaveChangesAsync(ct);
                        _logger.LogError(
                            retryEx,
                            "Ошибка при обогащении торгов Альфалот {TradeNumber} (PublicIds={PublicIds})",
                            bidding.TradeNumber,
                            FormatLotPublicIds(bidding));
                    }
                }
                catch (Exception ex)
                {
                    HandleError(bidding, ex);
                    await _context.SaveChangesAsync(ct);

                    _logger.LogError(
                        ex,
                        "Ошибка при обогащении торгов Альфалот {TradeNumber} (PublicIds={PublicIds})",
                        bidding.TradeNumber,
                        FormatLotPublicIds(bidding));
                }

                var delayMs = options.GetActionDelayMs();
                _logger.LogDebug("Пауза между торгами Альфалот: {DelayMs} мс", delayMs);
                await Task.Delay(delayMs, ct);
            }
        }
        finally
        {
            DisposeBrowserSession(ref driver, ref httpClient);
        }

        return true;
    }

    private (IWebDriver Driver, HttpClient HttpClient) CreateBrowserSession(TimeSpan pageTimeout, TimeSpan wafTimeout)
    {
        var driver = _webDriverFactory.CreateDriver();
        driver.Manage().Timeouts().PageLoad = pageTimeout;
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);

        AlfalotSeleniumNavigator.OpenAndWait(
            driver,
            AlfalotHtmlParser.PurchasesAllUrl,
            "tr.gridRow, tr.gridAltRow",
            wafTimeout);

        return (driver, CreateAuthedHttpClient(driver));
    }

    private static void DisposeBrowserSession(ref IWebDriver? driver, ref HttpClient? httpClient)
    {
        if (httpClient != null)
        {
            try { httpClient.Dispose(); } catch { /* ignore */ }
            httpClient = null;
        }

        if (driver != null)
        {
            try { driver.Quit(); } catch { /* ignore */ }
            try { driver.Dispose(); } catch { /* ignore */ }
            driver = null;
        }
    }

    private static bool IsDeadBrowserSession(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is not WebDriverException)
                continue;

            var msg = e.Message ?? string.Empty;
            if (msg.Contains("invalid session id", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("session deleted", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("chrome not reachable", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not connected to DevTools", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var pageTimeout = options.GetPageLoadTimeout();
        var wafTimeout = options.GetWafWaitTimeout();

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

        using var driver = _webDriverFactory.CreateDriver();
        driver.Manage().Timeouts().PageLoad = pageTimeout;
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);

        AlfalotSeleniumNavigator.OpenAndWait(
            driver,
            AlfalotHtmlParser.PurchasesAllUrl,
            "tr.gridRow, tr.gridAltRow",
            wafTimeout);

        using var httpClient = CreateAuthedHttpClient(driver);

        try
        {
            var result = await EnrichBiddingAsync(
                bidding,
                driver,
                httpClient,
                wafTimeout,
                forceReenrich: true,
                ct);

            ApplyBiddingEnrichmentStatus(bidding, result, options);

            if (bidding.EnrichmentState != null && bidding.IsEnriched == true)
                bidding.EnrichmentState.LastError = null;

            await _context.SaveChangesAsync(ct);

            LogBiddingEnrichmentSummary(bidding, result, afterSessionRecreate: false, manual: true);
        }
        finally
        {
            try { driver.Quit(); } catch { /* ignore */ }
        }
    }

    private void LogBiddingEnrichmentSummary(
        Bidding bidding,
        EnrichBiddingResult result,
        bool afterSessionRecreate,
        bool manual = false)
    {
        var prefix = manual
            ? "Ручное обогащение Альфалот"
            : afterSessionRecreate
                ? "Альфалот (после пересоздания сессии)"
                : "Альфалот";

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
        IWebDriver driver,
        HttpClient httpClient,
        TimeSpan wafTimeout,
        bool forceReenrich,
        CancellationToken ct)
    {
        var tradeNorm = AlfalotHtmlParser.NormalizeTradeNumber(bidding.TradeNumber);
        var options = _options.CurrentValue;
        var result = new EnrichBiddingResult();

        foreach (var lot in bidding.Lots)
        {
            if (!forceReenrich && lot.IsEnriched == true)
            {
                result.AlreadyEnriched++;
                continue;
            }

            var link = await FindLotLinkAsync(tradeNorm, lot.LotNumber, ct);
            if (link == null)
            {
                result.SkippedNoLink++;
                _logger.LogInformation(
                    "Альфалот: нет связки для торгов {TradeNumber}, лот {LotNumber} (PublicId={PublicId}). Ждём индексацию каталога.",
                    bidding.TradeNumber,
                    lot.LotNumber,
                    lot.PublicId);
                continue;
            }

            lot.Images.Clear();
            lot.Documents.Clear();
            lot.PriceSchedules.Clear();

            await Task.Delay(options.GetActionDelayMs(), ct);

            AlfalotSeleniumNavigator.OpenAndWait(
                driver,
                link.LotUrl,
                "fieldset, table.gridTable, a[rel^='prettyPhoto']",
                wafTimeout);

            var html = AlfalotSeleniumNavigator.GetPageHtml(driver);
            await ProcessImagesAsync(lot, html, httpClient, ct);

            if (IsPublicOffer(bidding.Type))
                ProcessPriceSchedule(lot, html);

            lot.IsEnriched = true;
            lot.EnrichedAt = DateTime.UtcNow;
            result.EnrichedLots++;

            _logger.LogInformation(
                "Альфалот: лот {LotNumber} (PublicId={PublicId}) обогащён. Фото={Images}, этапов графика={Schedules}",
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
        AlfalotEnrichmentOptions options)
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

        // Есть лоты без связки — это не hard-error, а ожидание каталога.
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
                    LastError = $"Ожидание связки в каталоге Альфалот ({result.SkippedNoLink} лот(ов))."
                };
            }
            else
            {
                bidding.EnrichmentState.MissingImagesAttemptCount++;
                bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
                bidding.EnrichmentState.LastError =
                    $"Ожидание связки в каталоге Альфалот ({result.SkippedNoLink} лот(ов)). " +
                    $"Попытка {bidding.EnrichmentState.MissingImagesAttemptCount}/{options.GetEffectiveMaxNoLinkWaitAttempts()}.";
            }

            var maxAttempts = options.GetEffectiveMaxNoLinkWaitAttempts();
            if (bidding.EnrichmentState.MissingImagesAttemptCount >= maxAttempts)
            {
                // Закрываем торги, чтобы не крутить бесконечно.
                // Лоты без связки остаются IsEnriched=false — их видно в БД.
                bidding.IsEnriched = true;
                bidding.EnrichedAt = DateTime.UtcNow;

                _logger.LogWarning(
                    "Альфалот {TradeNumber} (PublicIds={PublicIds}): прекращаем ожидание связки после {Attempts} попыток. " +
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

    private async Task<AlfalotLotLink?> FindLotLinkAsync(string tradeNorm, string? lotNumber, CancellationToken ct)
    {
        var lotNorm = AlfalotHtmlParser.NormalizeLotNumber(lotNumber);

        var link = await _context.AlfalotLotLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TradeNumberNormalized == tradeNorm && x.LotNumberNormalized == lotNorm,
                ct);

        if (link != null)
            return link;

        if (string.IsNullOrWhiteSpace(lotNumber) || lotNorm is "1" or "0")
        {
            var links = await _context.AlfalotLotLinks
                .AsNoTracking()
                .Where(x => x.TradeNumberNormalized == tradeNorm)
                .OrderBy(x => x.LotNumberNormalized)
                .Take(2)
                .ToListAsync(ct);

            if (links.Count == 1)
                return links[0];
        }

        return null;
    }

    private async Task ProcessImagesAsync(Lot lot, string html, HttpClient httpClient, CancellationToken ct)
    {
        var imageUrls = AlfalotHtmlParser.ExtractImageUrls(html);
        if (!imageUrls.Any())
        {
            _logger.LogInformation("Lot {LotNumber}: картинки на Альфалот не найдены.", lot.LotNumber);
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

                var imgBytes = await httpClient.GetByteArrayAsync(imgUrl, ct);
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
                _logger.LogWarning("Не удалось скачать картинку Альфалот {Url}: {Message}", imgUrl, ex.Message);
            }
        }
    }

    private void ProcessPriceSchedule(Lot lot, string html)
    {
        var rows = AlfalotHtmlParser.ExtractPriceSchedule(html);
        if (rows.Count == 0)
        {
            _logger.LogInformation("Lot {LotNumber}: график снижения цены на Альфалот не найден.", lot.LotNumber);
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
            "Lot {LotNumber}: сохранено {Count} этапов снижения цены с Альфалот.",
            lot.LotNumber,
            lot.PriceSchedules.Count);
    }

    private static HttpClient CreateAuthedHttpClient(IWebDriver driver)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = AlfalotSeleniumNavigator.ExportCookies(driver),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.Referrer = new Uri(AlfalotHtmlParser.BaseUrl + "/");

        return client;
    }

    private static bool IsPublicOffer(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return false;

        return type.Contains("Публичное", StringComparison.OrdinalIgnoreCase);
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
