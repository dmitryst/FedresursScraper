using System.Text.RegularExpressions;
using Lots.Application.Services.DebtScoring;
using Lots.Application.Services.DebtScoring.Models;
using Lots.Data.Entities;
using Lots.Data.Entities.DebtScoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services.DebtScoring;

public interface IDebtDocumentProcessingService
{
    Task<bool> ProcessPendingProfilesAsync(CancellationToken cancellationToken);
}

public class DebtDocumentProcessingService : IDebtDocumentProcessingService
{
    private readonly LotsDbContext _context;
    private readonly ILotsFileStorageService _fileStorage;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ICourtActEntityExtractor _entityExtractor;
    private readonly IPersonalDataProtector _personalDataProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<DebtScoringOptions> _options;
    private readonly ILogger<DebtDocumentProcessingService> _logger;

    private static readonly Regex DocumentUrlRegex = new(
        DebtScoringConstants.CourtDocumentUrlPattern[0],
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DebtDocumentProcessingService(
        LotsDbContext context,
        ILotsFileStorageService fileStorage,
        IDocumentTextExtractor textExtractor,
        ICourtActEntityExtractor entityExtractor,
        IPersonalDataProtector personalDataProtector,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<DebtScoringOptions> options,
        ILogger<DebtDocumentProcessingService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _textExtractor = textExtractor;
        _entityExtractor = entityExtractor;
        _personalDataProtector = personalDataProtector;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> ProcessPendingProfilesAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;

        var profiles = await _context.DebtLotProfiles
            .Include(p => p.Lot)
                .ThenInclude(l => l.Bidding)
                    .ThenInclude(b => b.LegalCase)
            .Include(p => p.Lot)
                .ThenInclude(l => l.Bidding)
                    .ThenInclude(b => b.Debtor)
            .Include(p => p.Lot)
                .ThenInclude(l => l.Documents)
            .Include(p => p.CourtDocuments)
            .Where(p => p.Status == DebtLotProcessingStatus.PendingDocuments
                || p.Status == DebtLotProcessingStatus.Failed)
            .Where(p => p.Attempts < options.MaxAttempts)
            .Where(p => p.NextAttemptAt == null || p.NextAttemptAt <= now)
            .OrderBy(p => p.CreatedAt)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return false;
        }

        foreach (var profile in profiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            profile.Status = DebtLotProcessingStatus.ProcessingDocuments;
            profile.UpdatedAt = now;
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                await ProcessProfileAsync(profile, options, cancellationToken);
            }
            catch (Exception ex)
            {
                profile.Attempts++;
                profile.Status = DebtLotProcessingStatus.Failed;
                profile.LastError = ex.Message;
                profile.NextAttemptAt = now.AddMinutes(options.RetryDelayMinutes);
                profile.UpdatedAt = DateTime.UtcNow;
                _logger.LogError(ex, "Debt document processing failed for lot {LotId}", profile.LotId);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    private async Task ProcessProfileAsync(
        DebtLotProfile profile,
        DebtScoringOptions options,
        CancellationToken cancellationToken)
    {
        var lot = profile.Lot;

        await ProcessLotMetadataAsync(profile, cancellationToken);

        var discoveredUrls = DiscoverDocumentUrls(lot.Description);
        var httpClient = _httpClientFactory.CreateClient("DebtScoring");

        foreach (var url in discoveredUrls)
        {
            if (profile.CourtDocuments.Any(d => d.SourceUrl == url))
            {
                continue;
            }

            await DownloadAndRegisterDocumentAsync(profile, lot, url, httpClient, cancellationToken);
        }

        foreach (var lotDocument in lot.Documents)
        {
            if (profile.CourtDocuments.Any(d => d.LotDocumentId == lotDocument.Id))
            {
                continue;
            }

            profile.CourtDocuments.Add(new DebtCourtDocument
            {
                Id = Guid.NewGuid(),
                LotId = profile.LotId,
                LotDocumentId = lotDocument.Id,
                Title = lotDocument.Title,
                Extension = lotDocument.Extension,
                DocumentType = DetectDocumentType(lotDocument.Title, lotDocument.Extension),
                ProcessingStatus = CourtDocumentProcessingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var pendingDocuments = await _context.DebtCourtDocuments
            .Where(d => d.LotId == profile.LotId)
            .Where(d => d.ProcessingStatus == CourtDocumentProcessingStatus.Pending
                || d.ProcessingStatus == CourtDocumentProcessingStatus.Failed)
            .Where(d => d.Attempts < options.MaxAttempts)
            .ToListAsync(cancellationToken);

        foreach (var document in pendingDocuments)
        {
            await ProcessCourtDocumentAsync(profile, document, httpClient, cancellationToken);
        }

        await FinalizeProfileAsync(profile, options, cancellationToken);
    }

    private async Task ProcessLotMetadataAsync(DebtLotProfile profile, CancellationToken cancellationToken)
    {
        var lot = profile.Lot;
        var bidding = lot.Bidding;

        var staleEntities = await _context.DebtExtractedEntities
            .Where(e => e.LotId == profile.LotId && e.CourtDocumentId == null)
            .ToListAsync(cancellationToken);

        if (staleEntities.Count > 0)
        {
            _context.DebtExtractedEntities.RemoveRange(staleEntities);
        }

        ApplyBiddingContext(profile, bidding);

        var descriptionText = BuildDescriptionText(lot);
        if (!string.IsNullOrWhiteSpace(descriptionText))
        {
            var parsed = _entityExtractor.Extract(descriptionText);
            await SaveExtractedEntitiesAsync(profile, courtDocument: null, parsed, cancellationToken);
            ApplyAggregates(profile, parsed);
        }

        profile.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private void ApplyBiddingContext(DebtLotProfile profile, Bidding bidding)
    {
        var caseNumber = bidding.LegalCase?.CaseNumber;
        if (!string.IsNullOrWhiteSpace(caseNumber) && caseNumber != "неизвестно")
        {
            profile.CaseNumber = caseNumber;
            SaveBiddingEntity(profile, ExtractedEntityType.CaseNumber, caseNumber, 1.0);
        }

        var debtor = bidding.Debtor;
        if (debtor == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(debtor.Name))
        {
            SaveBiddingEntity(profile, ExtractedEntityType.DebtorName, debtor.Name, 1.0);
        }

        if (!string.IsNullOrWhiteSpace(debtor.Inn))
        {
            SaveBiddingEntity(profile, ExtractedEntityType.Inn, debtor.Inn, 1.0);
        }

        if (!string.IsNullOrWhiteSpace(debtor.Snils))
        {
            SaveBiddingEntity(profile, ExtractedEntityType.Snils, debtor.Snils, 1.0);
        }

        if (!string.IsNullOrWhiteSpace(debtor.Ogrn))
        {
            SaveBiddingEntity(profile, ExtractedEntityType.Ogrn, debtor.Ogrn, 1.0);
        }
    }

    private void SaveBiddingEntity(DebtLotProfile profile, ExtractedEntityType type, string value, double confidence)
    {
        var storedValue = value;
        var isEncrypted = false;

        if (_personalDataProtector.IsPersonalData(type))
        {
            storedValue = _personalDataProtector.Protect(value);
            isEncrypted = true;
        }

        _context.DebtExtractedEntities.Add(new DebtExtractedEntity
        {
            Id = Guid.NewGuid(),
            LotId = profile.LotId,
            EntityType = type,
            Value = storedValue,
            IsEncrypted = isEncrypted,
            Confidence = confidence,
            Source = EntityExtractionSource.Fedresurs,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string BuildDescriptionText(Lot lot)
    {
        if (!string.IsNullOrWhiteSpace(lot.Description) && lot.Description != "не найдено")
        {
            return lot.Description;
        }

        return lot.Title ?? string.Empty;
    }

    private static bool HasExtractedMetadata(DebtLotProfile profile) =>
        profile.DebtNominal.HasValue
        || !string.IsNullOrWhiteSpace(profile.CaseNumber)
        || !string.IsNullOrWhiteSpace(profile.DebtBasis);

    private async Task DownloadAndRegisterDocumentAsync(
        DebtLotProfile profile,
        Lot lot,
        string url,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension)
            || !DebtScoringConstants.CourtDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var bytes = await httpClient.GetByteArrayAsync(url, cancellationToken);
        var fileName = $"lots/{lot.Id}/court-docs/{Guid.NewGuid()}{extension.ToLowerInvariant()}";
        var contentType = GetContentType(extension);
        var s3Url = await _fileStorage.UploadAsync(bytes, fileName, contentType);

        var title = Path.GetFileName(new Uri(url).AbsolutePath);
        var lotDocument = new LotDocument
        {
            Id = Guid.NewGuid(),
            LotId = lot.Id,
            Url = s3Url,
            Title = title,
            Extension = extension,
            CreatedAt = DateTime.UtcNow,
        };

        lot.Documents.Add(lotDocument);

        profile.CourtDocuments.Add(new DebtCourtDocument
        {
            Id = Guid.NewGuid(),
            LotId = profile.LotId,
            LotDocumentId = lotDocument.Id,
            SourceUrl = url,
            Title = title,
            Extension = extension,
            DocumentType = DetectDocumentType(title, extension),
            ProcessingStatus = CourtDocumentProcessingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private async Task ProcessCourtDocumentAsync(
        DebtLotProfile profile,
        DebtCourtDocument document,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        document.ProcessingStatus = CourtDocumentProcessingStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var extension = document.Extension ?? ".pdf";
            byte[] fileContent;

            if (document.LotDocumentId.HasValue)
            {
                var lotDocument = await _context.Documents
                    .FirstAsync(d => d.Id == document.LotDocumentId.Value, cancellationToken);

                fileContent = await DownloadFromStorageAsync(lotDocument.Url, httpClient, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(document.SourceUrl))
            {
                fileContent = await httpClient.GetByteArrayAsync(document.SourceUrl, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Документ не имеет источника для скачивания");
            }

            if (!_textExtractor.CanExtract(extension))
            {
                document.ProcessingStatus = CourtDocumentProcessingStatus.Skipped;
                document.LastError = $"Формат {extension} не поддерживается";
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            var extraction = await _textExtractor.ExtractAsync(fileContent, extension, cancellationToken);
            if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
            {
                document.Attempts++;
                document.ProcessingStatus = CourtDocumentProcessingStatus.Failed;
                document.LastError = extraction.Error ?? "Не удалось извлечь текст";
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            document.OcrText = extraction.Text;
            document.OcrConfidence = extraction.Confidence;
            document.ProcessedAt = DateTime.UtcNow;
            document.ProcessingStatus = CourtDocumentProcessingStatus.Completed;

            var parsed = _entityExtractor.Extract(extraction.Text);
            await SaveExtractedEntitiesAsync(profile, document, parsed, cancellationToken);
            ApplyAggregates(profile, parsed);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            document.Attempts++;
            document.ProcessingStatus = CourtDocumentProcessingStatus.Failed;
            document.LastError = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Failed to process court document {DocumentId} for lot {LotId}", document.Id, profile.LotId);
        }
    }

    private async Task FinalizeProfileAsync(
        DebtLotProfile profile,
        DebtScoringOptions options,
        CancellationToken cancellationToken)
    {
        var documents = await _context.DebtCourtDocuments
            .Where(d => d.LotId == profile.LotId)
            .ToListAsync(cancellationToken);

        var hasCompletedDocument = documents.Any(d => d.ProcessingStatus == CourtDocumentProcessingStatus.Completed);
        var hasMetadata = HasExtractedMetadata(profile);
        var allTerminal = documents.Count == 0 || documents.All(d =>
            d.ProcessingStatus is CourtDocumentProcessingStatus.Completed
                or CourtDocumentProcessingStatus.Skipped
                or CourtDocumentProcessingStatus.Failed);

        profile.UpdatedAt = DateTime.UtcNow;

        if (hasMetadata || hasCompletedDocument)
        {
            if (profile.DebtNominal.HasValue && profile.DebtNominal.Value < options.MinDebtNominal)
            {
                profile.Status = DebtLotProcessingStatus.Rejected;
                profile.RejectionReason =
                    $"Номинал {profile.DebtNominal.Value:N0} руб. ниже порога {options.MinDebtNominal:N0} руб.";
            }
            else
            {
                profile.Status = DebtLotProcessingStatus.PendingEnrichment;
                profile.LastError = null;
            }

            profile.NextAttemptAt = null;
        }
        else if (documents.Count == 0)
        {
            profile.Attempts++;
            profile.Status = DebtLotProcessingStatus.Failed;
            profile.LastError = "Не удалось извлечь данные из описания лота и торгов";
            profile.NextAttemptAt = DateTime.UtcNow.AddMinutes(options.RetryDelayMinutes);
        }
        else if (allTerminal)
        {
            profile.Attempts++;
            profile.Status = DebtLotProcessingStatus.Failed;
            profile.LastError = "Не удалось извлечь данные из описания и судебных документов";
            profile.NextAttemptAt = DateTime.UtcNow.AddMinutes(options.RetryDelayMinutes);
        }
        else
        {
            profile.Status = DebtLotProcessingStatus.PendingDocuments;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Debt lot profile finalized: lot={LotId}, status={Status}, docs={DocCount}",
            profile.LotId,
            profile.Status,
            documents.Count);
    }

    private async Task SaveExtractedEntitiesAsync(
        DebtLotProfile profile,
        DebtCourtDocument? courtDocument,
        CourtActExtractionResult parsed,
        CancellationToken cancellationToken)
    {
        foreach (var entity in parsed.Entities)
        {
            var value = entity.Value;
            var isEncrypted = false;

            if (_personalDataProtector.IsPersonalData(entity.EntityType))
            {
                value = _personalDataProtector.Protect(entity.Value);
                isEncrypted = true;
            }

            _context.DebtExtractedEntities.Add(new DebtExtractedEntity
            {
                Id = Guid.NewGuid(),
                LotId = profile.LotId,
                CourtDocumentId = courtDocument?.Id,
                EntityType = entity.EntityType,
                Value = value,
                IsEncrypted = isEncrypted,
                Confidence = entity.Confidence,
                Source = entity.Source,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await Task.CompletedTask;
    }

    private static void ApplyAggregates(DebtLotProfile profile, CourtActExtractionResult parsed)
    {
        if (parsed.DebtNominal.HasValue)
        {
            profile.DebtNominal = parsed.DebtNominal;
        }

        var caseNumber = parsed.Entities
            .Where(e => e.EntityType == ExtractedEntityType.CaseNumber)
            .OrderByDescending(e => e.Confidence)
            .Select(e => e.Value)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(caseNumber))
        {
            profile.CaseNumber = caseNumber;
        }

        var debtBasis = parsed.Entities
            .Where(e => e.EntityType == ExtractedEntityType.DebtBasis)
            .OrderByDescending(e => e.Confidence)
            .Select(e => e.Value)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(debtBasis))
        {
            profile.DebtBasis = debtBasis;
        }
    }

    private static List<string> DiscoverDocumentUrls(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        return DocumentUrlRegex.Matches(description)
            .Select(m => m.Value.Trim().TrimEnd(')', ']', '.', ','))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CourtDocumentType DetectDocumentType(string? title, string? extension)
    {
        var haystack = $"{title} {extension}".ToLowerInvariant();

        if (haystack.Contains("исполнительн"))
        {
            return CourtDocumentType.WritOfExecution;
        }

        if (haystack.Contains("определен"))
        {
            return CourtDocumentType.CourtOrder;
        }

        if (haystack.Contains("решени"))
        {
            return CourtDocumentType.CourtDecision;
        }

        return CourtDocumentType.Unknown;
    }

    private static async Task<byte[]> DownloadFromStorageAsync(
        string url,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        return await httpClient.GetByteArrayAsync(url, cancellationToken);
    }

    private static string GetContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };
}
