using Lots.Application.Services.DebtScoring;
using Lots.Application.Services.DebtScoring.Enrichment;
using Lots.Data.Entities;
using Lots.Data.Entities.DebtScoring;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Services.DebtScoring.Enrichment;

public interface IDebtEnrichmentIdentityResolver
{
    Task<DebtEnrichmentContext> BuildContextAsync(
        DebtLotProfile profile,
        DebtorEnrichmentProfile enrichment,
        DebtScoringOptions options,
        CancellationToken cancellationToken);
}

public class DebtEnrichmentIdentityResolver : IDebtEnrichmentIdentityResolver
{
    private readonly LotsDbContext _context;
    private readonly IPersonalDataProtector _personalDataProtector;

    public DebtEnrichmentIdentityResolver(
        LotsDbContext context,
        IPersonalDataProtector personalDataProtector)
    {
        _context = context;
        _personalDataProtector = personalDataProtector;
    }

    public async Task<DebtEnrichmentContext> BuildContextAsync(
        DebtLotProfile profile,
        DebtorEnrichmentProfile enrichment,
        DebtScoringOptions options,
        CancellationToken cancellationToken)
    {
        var lot = await _context.Lots
            .Include(l => l.Bidding)
                .ThenInclude(b => b.Debtor)
            .Include(l => l.Bidding)
                .ThenInclude(b => b.LegalCase)
            .FirstAsync(l => l.Id == profile.LotId, cancellationToken);

        var debtor = lot.Bidding.Debtor;
        var extractedEntities = await _context.DebtExtractedEntities
            .Where(e => e.LotId == profile.LotId)
            .ToListAsync(cancellationToken);

        var resolvedInn = ResolveValue(debtor?.Inn, extractedEntities, ExtractedEntityType.Inn);
        var resolvedName = ResolveValue(debtor?.Name, extractedEntities, ExtractedEntityType.DebtorName);
        var resolvedSnils = ResolveValue(debtor?.Snils, extractedEntities, ExtractedEntityType.Snils);
        var caseNumber = FirstNonEmpty(
            profile.CaseNumber,
            lot.Bidding.LegalCase?.CaseNumber,
            ResolveValue(null, extractedEntities, ExtractedEntityType.CaseNumber));

        enrichment.SubjectId = debtor?.Id;
        enrichment.DebtorType = debtor?.Type ?? InferDebtorType(resolvedInn, resolvedName);
        enrichment.ResolvedInn = StoreProtected(resolvedInn, out var innEncrypted);
        enrichment.IsResolvedInnEncrypted = innEncrypted;
        enrichment.ResolvedName = StoreProtected(resolvedName, out var nameEncrypted);
        enrichment.IsResolvedNameEncrypted = nameEncrypted;
        enrichment.ResolvedSnils = StoreProtected(resolvedSnils, out var snilsEncrypted);
        enrichment.IsResolvedSnilsEncrypted = snilsEncrypted;

        return new DebtEnrichmentContext
        {
            Profile = profile,
            Enrichment = enrichment,
            Options = options,
            Debtor = debtor,
            ResolvedInn = resolvedInn,
            ResolvedName = resolvedName,
            ResolvedSnils = resolvedSnils,
            CaseNumber = caseNumber,
        };
    }

    private string? ResolveValue(
        string? primary,
        IReadOnlyList<DebtExtractedEntity> entities,
        ExtractedEntityType type)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        var entity = entities
            .Where(e => e.EntityType == type)
            .OrderByDescending(e => e.Confidence)
            .FirstOrDefault();

        if (entity == null)
        {
            return null;
        }

        return entity.IsEncrypted
            ? _personalDataProtector.Unprotect(entity.Value)
            : entity.Value;
    }

    private string? StoreProtected(string? value, out bool isEncrypted)
    {
        isEncrypted = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        isEncrypted = true;
        return _personalDataProtector.Protect(value.Trim());
    }

    private static SubjectType InferDebtorType(string? inn, string? name)
    {
        if (!string.IsNullOrWhiteSpace(inn))
        {
            var digits = inn.Where(char.IsDigit).Count();
            return digits == 10 ? SubjectType.Company : SubjectType.Individual;
        }

        return SubjectType.Individual;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "неизвестно")
            {
                return value.Trim();
            }
        }

        return null;
    }
}
