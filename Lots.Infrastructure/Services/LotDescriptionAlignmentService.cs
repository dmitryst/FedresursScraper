using FedresursScraper.Services.Models;
using Lots.Application.Interfaces;
using Lots.Application.Services.DebtScoring;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services;

public class LotDescriptionAlignmentService : ILotDescriptionAlignmentService
{
    private readonly LotsDbContext _dbContext;
    private readonly IParserScrapeClient _parserScrapeClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ILotPropertyDescriptionSummarizer _descriptionSummarizer;
    private readonly ILogger<LotDescriptionAlignmentService> _logger;

    public LotDescriptionAlignmentService(
        LotsDbContext dbContext,
        IParserScrapeClient parserScrapeClient,
        IHttpClientFactory httpClientFactory,
        IDocumentTextExtractor textExtractor,
        ILotPropertyDescriptionSummarizer descriptionSummarizer,
        ILogger<LotDescriptionAlignmentService> logger)
    {
        _dbContext = dbContext;
        _parserScrapeClient = parserScrapeClient;
        _httpClientFactory = httpClientFactory;
        _textExtractor = textExtractor;
        _descriptionSummarizer = descriptionSummarizer;
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
        var scrapeCache = new Dictionary<Guid, BankruptMessageScrapeResult>();

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
            .Include(l => l.Documents)
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

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            lot.Latitude = request.Latitude.Value;
            lot.Longitude = request.Longitude.Value;
        }

        if (request.Attachments?.Count > 0)
        {
            await SaveSelectedAttachmentsAsync(lot, request.Attachments, cancellationToken);
        }

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

    private async Task BuildPreviewAsync(
        Lot lot,
        Dictionary<Guid, BankruptMessageScrapeResult> scrapeCache,
        CancellationToken cancellationToken,
        LotDescriptionAlignmentPreviewDto preview)
    {
        var messageId = lot.Bidding?.BankruptMessageId ?? Guid.Empty;
        if (messageId == Guid.Empty)
        {
            preview.Error = "У торгов нет ссылки на объявление Федресурса (BankruptMessageId).";
            return;
        }

        preview.FedresursUrl = $"https://fedresurs.ru/bankruptmessages/{messageId}";

        try
        {
            if (!scrapeCache.TryGetValue(messageId, out var scrapeResult))
            {
                scrapeResult = await _parserScrapeClient.GetBankruptMessageDataAsync(messageId, cancellationToken);
                scrapeCache[messageId] = scrapeResult;
            }

            var match = FindMatchingLot(scrapeResult.Lots, lot.LotNumber, lot.StartPrice);
            if (match == null)
            {
                preview.Error = $"Лот не найден на странице Федресурса (номер: {lot.LotNumber ?? "—"}, цена: {lot.StartPrice}).";
                return;
            }

            var (tableDescription, scrapedViewing) = LotDescriptionTextHelper.SplitDescriptionAndViewing(match.Description);
            preview.TableDescription = tableDescription;
            preview.IsReferralDescription = LotPropertyDocumentHelper.IsPropertyListReferral(tableDescription);

            foreach (var attachment in scrapeResult.Attachments)
            {
                var attachmentPreview = await ProcessAttachmentPreviewAsync(
                    attachment,
                    preview.FedresursUrl,
                    cancellationToken);
                preview.Attachments.Add(attachmentPreview);
            }

            var documentText = LotPropertyDocumentHelper.MergeExtractedTexts(
                preview.Attachments.Where(a => a.UseForDescription).Select(a => a.DescriptionText));

            var proposedFromTable = string.IsNullOrWhiteSpace(tableDescription) ||
                                    tableDescription.Equals("не найдено", StringComparison.OrdinalIgnoreCase)
                ? null
                : tableDescription.Trim();

            var proposedDescription = LotPropertyDocumentHelper.BuildProposedDescription(
                proposedFromTable,
                documentText);

            if (string.IsNullOrWhiteSpace(proposedDescription))
            {
                if (preview.IsReferralDescription && preview.Attachments.Count == 0)
                {
                    preview.Error =
                        "Описание ссылается на внешний перечень имущества, но файлы на странице Федресурса не найдены. Загрузите документ вручную.";
                    return;
                }

                if (preview.Attachments.Count > 0)
                {
                    preview.ProposedDescription = string.Empty;
                    preview.Error =
                        "Выберите файлы «В описание» или введите текст описания вручную.";
                }
                else
                {
                    preview.Error = "На Федресурсе не удалось получить описание имущества для этого лота.";
                    return;
                }
            }
            else
            {
                preview.ProposedDescription = proposedDescription;
            }
            preview.ProposedViewingProcedure = LotDescriptionTextHelper.MergeViewingProcedureParts(
                lot.Bidding?.ViewingProcedure,
                lot.Description,
                scrapedViewing);

            if (preview.IsReferralDescription && !string.IsNullOrWhiteSpace(proposedFromTable))
            {
                preview.ProposedViewingProcedure = LotDescriptionTextHelper.MergeViewingProcedureParts(
                    preview.ProposedViewingProcedure,
                    proposedFromTable);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка предпросмотра выравнивания для лота {PublicId}", lot.PublicId);
            preview.Error = ex.Message;
        }
    }

    private async Task<LotDescriptionAlignmentPreviewDto> BuildPreviewAsync(
        Lot lot,
        Dictionary<Guid, BankruptMessageScrapeResult> scrapeCache,
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

        await BuildPreviewAsync(lot, scrapeCache, cancellationToken, preview);
        return preview;
    }

    private async Task<AlignmentAttachmentPreviewDto> ProcessAttachmentPreviewAsync(
        FedresursAttachmentInfo attachment,
        string? referer,
        CancellationToken cancellationToken)
    {
        var preview = new AlignmentAttachmentPreviewDto
        {
            Title = attachment.Title,
            Url = attachment.Url,
            Extension = attachment.Extension,
        };

        if (!_textExtractor.CanExtract(attachment.Extension))
        {
            preview.ExtractionError =
                $"Формат {attachment.Extension} не поддерживается для автоматического извлечения текста.";
        }
        else
        {
            try
            {
                var bytes = await DownloadFileAsync(attachment.Url, referer, cancellationToken);

                var extraction = await _textExtractor.ExtractAsync(bytes, attachment.Extension, cancellationToken);
                if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
                {
                    preview.ExtractionError = extraction.Error ?? "Не удалось извлечь текст из файла.";
                }
                else
                {
                    var rawText = extraction.Text.Trim();
                    preview.ExtractedText = LotPropertyDocumentHelper.TruncateForPreview(rawText);

                    if (LotPropertyDocumentHelper.NeedsSummarization(rawText))
                    {
                        var summary = await _descriptionSummarizer.SummarizeAsync(rawText, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(summary.Summary))
                        {
                            preview.IsSummarized = true;
                            preview.DescriptionText = summary.Summary;
                        }
                        else
                        {
                            preview.SummarizationError = summary.Error;
                            preview.DescriptionText = LotPropertyDocumentHelper.TruncateForPreview(rawText, 2000);
                        }
                    }
                    else
                    {
                        preview.DescriptionText = rawText;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обработать вложение {Url}", attachment.Url);
                preview.ExtractionError = ex.Message;
            }
        }

        preview.SelectedForDownload = LotPropertyDocumentHelper.GetDefaultSelectedForDownload(preview.Title);
        preview.UseForDescription = LotPropertyDocumentHelper.GetDefaultUseForDescription(
            preview.Title,
            !string.IsNullOrWhiteSpace(preview.DescriptionText));

        return preview;
    }

    private async Task SaveSelectedAttachmentsAsync(
        Lot lot,
        IReadOnlyList<ApplyAlignmentAttachmentRequest> attachments,
        CancellationToken cancellationToken)
    {
        var existingSourceUrls = lot.Documents
            .Where(d => !string.IsNullOrWhiteSpace(d.SourceUrl))
            .Select(d => d.SourceUrl!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.SourceUrl))
                continue;

            if (!LotDocumentLinkHelper.IsFedresursDocumentUrl(attachment.SourceUrl))
            {
                _logger.LogWarning(
                    "Пропущено вложение с неподдерживаемым SourceUrl для лота {PublicId}: {Url}",
                    lot.PublicId,
                    attachment.SourceUrl);
                continue;
            }

            if (!existingSourceUrls.Add(attachment.SourceUrl))
                continue;

            var extension = attachment.Extension;
            if (string.IsNullOrWhiteSpace(extension))
                extension = LotPropertyDocumentHelper.DetectDocumentExtension(attachment.SourceUrl, attachment.Title) ?? ".bin";

            _dbContext.Documents.Add(new LotDocument
            {
                Id = Guid.NewGuid(),
                LotId = lot.Id,
                SourceUrl = attachment.SourceUrl,
                Title = string.IsNullOrWhiteSpace(attachment.Title) ? "Документ" : attachment.Title,
                Extension = extension,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await Task.CompletedTask;
    }

    private async Task<byte[]> DownloadFileAsync(string url, string? referer, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("FedresursDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(referer))
            request.Headers.TryAddWithoutValidation("Referer", referer);

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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

        return System.Text.RegularExpressions.Regex.Replace(lotNumber.Trim(), @"(?i)^лот\s*№?\s*", "").Trim();
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
