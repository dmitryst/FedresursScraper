using System.Text.RegularExpressions;
using FedresursScraper.Services;
using FedresursScraper.Services.Models;
using Lots.Application.Interfaces;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services;

public class LotDescriptionAlignmentService : ILotDescriptionAlignmentService
{
    private readonly LotsDbContext _dbContext;
    private readonly IParserScrapeClient _parserScrapeClient;
    private readonly ILogger<LotDescriptionAlignmentService> _logger;

    public LotDescriptionAlignmentService(
        LotsDbContext dbContext,
        IParserScrapeClient parserScrapeClient,
        ILogger<LotDescriptionAlignmentService> logger)
    {
        _dbContext = dbContext;
        _parserScrapeClient = parserScrapeClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LotDescriptionAlignmentPreviewDto>> PreviewAsync(
        IReadOnlyList<int> publicIds,
        CancellationToken cancellationToken = default)
    {
        if (publicIds.Count == 0)
            return [];

        if (publicIds.Count > 20)
            throw new ArgumentException("За один раз можно выровнять не более 20 лотов.");

        var lots = await _dbContext.Lots
            .AsNoTracking()
            .Include(l => l.Bidding)
            .Where(l => publicIds.Contains(l.PublicId) && l.NeedsDescriptionReview)
            .ToListAsync(cancellationToken);

        var lotsByPublicId = lots.ToDictionary(l => l.PublicId);
        var scrapeCache = new Dictionary<Guid, IReadOnlyList<LotInfo>>();

        var results = new List<LotDescriptionAlignmentPreviewDto>();

        foreach (var publicId in publicIds)
        {
            if (!lotsByPublicId.TryGetValue(publicId, out var lot))
            {
                results.Add(new LotDescriptionAlignmentPreviewDto
                {
                    PublicId = publicId,
                    Error = "Лот не найден или не требует доработки описания."
                });
                continue;
            }

            results.Add(await BuildPreviewAsync(lot, scrapeCache, cancellationToken));
        }

        return results;
    }

    public async Task<LotDescriptionAlignmentPreviewDto?> ApplyAsync(
        ApplyLotDescriptionAlignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var lot = await _dbContext.Lots
            .Include(l => l.Bidding)
            .FirstOrDefaultAsync(l => l.PublicId == request.PublicId && l.NeedsDescriptionReview, cancellationToken);

        if (lot == null)
            return null;

        if (lot.Bidding == null)
            throw new InvalidOperationException("Торги для лота не найдены.");

        lot.Description = request.Description.Trim();
        lot.Bidding.ViewingProcedure = string.IsNullOrWhiteSpace(request.ViewingProcedure)
            ? null
            : request.ViewingProcedure.Trim();
        lot.Slug = null;
        lot.NeedsDescriptionReview = false;

        var classificationState = await _dbContext.LotClassificationStates
            .FirstOrDefaultAsync(s => s.LotId == lot.Id, cancellationToken);

        if (classificationState == null)
        {
            classificationState = new LotClassificationState
            {
                LotId = lot.Id,
                Status = ClassificationStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTime.UtcNow
            };
            _dbContext.LotClassificationStates.Add(classificationState);
        }
        else
        {
            classificationState.Status = ClassificationStatus.Pending;
            classificationState.Attempts = 0;
            classificationState.NextAttemptAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Применено выравнивание описания для лота {PublicId}.", request.PublicId);

        return new LotDescriptionAlignmentPreviewDto
        {
            PublicId = lot.PublicId,
            LotId = lot.Id,
            LotNumber = lot.LotNumber,
            StartPrice = lot.StartPrice,
            CurrentDescription = lot.Description,
            CurrentViewingProcedure = lot.Bidding.ViewingProcedure,
            ProposedDescription = lot.Description,
            ProposedViewingProcedure = lot.Bidding.ViewingProcedure,
        };
    }

    private async Task<LotDescriptionAlignmentPreviewDto> BuildPreviewAsync(
        Lot lot,
        Dictionary<Guid, IReadOnlyList<LotInfo>> scrapeCache,
        CancellationToken cancellationToken)
    {
        var preview = new LotDescriptionAlignmentPreviewDto
        {
            PublicId = lot.PublicId,
            LotId = lot.Id,
            LotNumber = lot.LotNumber,
            StartPrice = lot.StartPrice,
            CurrentDescription = lot.Description,
            CurrentViewingProcedure = lot.Bidding?.ViewingProcedure,
        };

        var messageId = lot.Bidding?.BankruptMessageId ?? Guid.Empty;
        if (messageId == Guid.Empty)
        {
            preview.Error = "У торгов нет ссылки на объявление Федресурса (BankruptMessageId).";
            return preview;
        }

        preview.FedresursUrl = $"https://fedresurs.ru/bankruptmessages/{messageId}";

        try
        {
            if (!scrapeCache.TryGetValue(messageId, out var scrapedLots))
            {
                scrapedLots = await _parserScrapeClient.GetLotsFromBankruptMessageAsync(messageId, cancellationToken);
                scrapeCache[messageId] = scrapedLots;
            }

            var match = FindMatchingLot(scrapedLots, lot.LotNumber, lot.StartPrice);
            if (match == null)
            {
                preview.Error = $"Лот не найден на странице Федресурса (номер: {lot.LotNumber ?? "—"}, цена: {lot.StartPrice}).";
                return preview;
            }

            var (scrapedDescription, scrapedViewing) = LotDescriptionTextHelper.SplitDescriptionAndViewing(match.Description);

            if (string.IsNullOrWhiteSpace(scrapedDescription) ||
                scrapedDescription.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
            {
                preview.Error = "На Федресурсе не удалось получить описание имущества для этого лота.";
                return preview;
            }

            preview.ProposedDescription = scrapedDescription.Trim();
            preview.ProposedViewingProcedure = LotDescriptionTextHelper.MergeViewingProcedureParts(
                lot.Bidding?.ViewingProcedure,
                lot.Description,
                scrapedViewing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка предпросмотра выравнивания для лота {PublicId}", lot.PublicId);
            preview.Error = ex.Message;
        }

        return preview;
    }

    internal static LotInfo? FindMatchingLot(
        IReadOnlyList<LotInfo> scrapedLots,
        string? lotNumber,
        decimal? startPrice)
    {
        if (scrapedLots.Count == 0)
            return null;

        var normalizedTarget = NormalizeLotNumber(lotNumber);

        var candidates = scrapedLots.Where(s =>
            LotNumbersMatch(normalizedTarget, NormalizeLotNumber(s.Number)) &&
            PricesMatch(startPrice, s.StartPrice)).ToList();

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
            return candidates[0];

        if (!string.IsNullOrEmpty(normalizedTarget))
        {
            var byNumber = scrapedLots
                .Where(s => LotNumbersMatch(normalizedTarget, NormalizeLotNumber(s.Number)))
                .ToList();
            if (byNumber.Count == 1)
                return byNumber[0];
        }

        if (startPrice.HasValue)
        {
            var byPrice = scrapedLots.Where(s => PricesMatch(startPrice, s.StartPrice)).ToList();
            if (byPrice.Count == 1)
                return byPrice[0];
        }

        return null;
    }

    private static string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
            return string.Empty;

        return Regex.Replace(lotNumber.Trim(), @"(?i)^лот\s*№?\s*", "").Trim();
    }

    private static bool LotNumbersMatch(string a, string b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool PricesMatch(decimal? a, decimal? b)
    {
        if (!a.HasValue || !b.HasValue)
            return false;

        return Math.Abs(a.Value - b.Value) < 0.01m;
    }
}
