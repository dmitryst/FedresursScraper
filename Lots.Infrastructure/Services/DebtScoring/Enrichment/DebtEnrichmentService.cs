using Lots.Application.Services.DebtScoring;
using Lots.Application.Services.DebtScoring.Enrichment;
using Lots.Data.Entities.DebtScoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services.DebtScoring.Enrichment;

public interface IDebtEnrichmentService
{
    Task<bool> ProcessPendingProfilesAsync(CancellationToken cancellationToken);
}

public class DebtEnrichmentService : IDebtEnrichmentService
{
    private readonly LotsDbContext _context;
    private readonly IDebtEnrichmentIdentityResolver _identityResolver;
    private readonly IEnumerable<IDebtEnrichmentStep> _steps;
    private readonly IOptionsMonitor<DebtScoringOptions> _options;
    private readonly ILogger<DebtEnrichmentService> _logger;

    public DebtEnrichmentService(
        LotsDbContext context,
        IDebtEnrichmentIdentityResolver identityResolver,
        IEnumerable<IDebtEnrichmentStep> steps,
        IOptionsMonitor<DebtScoringOptions> options,
        ILogger<DebtEnrichmentService> logger)
    {
        _context = context;
        _identityResolver = identityResolver;
        _steps = steps.OrderBy(s => s.Kind).ToList();
        _options = options;
        _logger = logger;
    }

    public async Task<bool> ProcessPendingProfilesAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;

        var profiles = await (
            from profile in _context.DebtLotProfiles
            join enrichment in _context.DebtorEnrichmentProfiles
                on profile.LotId equals enrichment.LotId into enrichmentJoin
            from enrichment in enrichmentJoin.DefaultIfEmpty()
            where profile.Status == DebtLotProcessingStatus.PendingEnrichment
                || profile.Status == DebtLotProcessingStatus.DocumentsProcessed
            where enrichment == null
                || (enrichment.Attempts < options.EnrichmentMaxAttempts
                    && (enrichment.NextAttemptAt == null || enrichment.NextAttemptAt <= now))
            orderby profile.CreatedAt
            select profile)
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

            try
            {
                await ProcessProfileAsync(profile, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debt enrichment failed for lot {LotId}", profile.LotId);
                await MarkEnrichmentFailedAsync(profile, ex.Message, options, cancellationToken);
            }
        }

        return true;
    }

    private async Task ProcessProfileAsync(
        DebtLotProfile profile,
        DebtScoringOptions options,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        profile.Status = DebtLotProcessingStatus.ProcessingEnrichment;
        profile.UpdatedAt = now;

        var enrichment = await _context.DebtorEnrichmentProfiles
            .FirstOrDefaultAsync(e => e.LotId == profile.LotId, cancellationToken);

        if (enrichment == null)
        {
            enrichment = new DebtorEnrichmentProfile
            {
                LotId = profile.LotId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _context.DebtorEnrichmentProfiles.Add(enrichment);
        }

        enrichment.UpdatedAt = now;
        await _context.SaveChangesAsync(cancellationToken);

        var context = await _identityResolver.BuildContextAsync(profile, enrichment, options, cancellationToken);

        var hasFailure = false;
        string? lastError = null;

        foreach (var step in _steps)
        {
            SetStepStatus(enrichment, step.Kind, DebtEnrichmentStepStatus.InProgress);
            await _context.SaveChangesAsync(cancellationToken);

            var result = await step.ExecuteAsync(context, cancellationToken);
            SetStepStatus(enrichment, step.Kind, result.Status);

            if (result.Status == DebtEnrichmentStepStatus.Failed)
            {
                hasFailure = true;
                lastError = $"{step.StepName}: {result.Error}";
            }

            enrichment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        if (hasFailure)
        {
            await MarkEnrichmentFailedAsync(profile, lastError ?? "Enrichment step failed", options, cancellationToken);
            return;
        }

        profile.Status = DebtLotProcessingStatus.EnrichmentCompleted;
        profile.LastError = null;
        profile.UpdatedAt = DateTime.UtcNow;
        enrichment.LastError = null;
        enrichment.NextAttemptAt = null;
        enrichment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Debt enrichment completed for lot {LotId}. INN resolved: {HasInn}, case: {CaseNumber}",
            profile.LotId,
            !string.IsNullOrWhiteSpace(context.ResolvedInn),
            context.CaseNumber);
    }

    private async Task MarkEnrichmentFailedAsync(
        DebtLotProfile profile,
        string error,
        DebtScoringOptions options,
        CancellationToken cancellationToken)
    {
        var enrichment = await _context.DebtorEnrichmentProfiles
            .FirstOrDefaultAsync(e => e.LotId == profile.LotId, cancellationToken);

        if (enrichment != null)
        {
            enrichment.Attempts++;
            enrichment.LastError = error;
            enrichment.NextAttemptAt = DateTime.UtcNow.AddMinutes(options.EnrichmentRetryDelayMinutes);
            enrichment.UpdatedAt = DateTime.UtcNow;
        }

        profile.Status = DebtLotProcessingStatus.PendingEnrichment;
        profile.LastError = error;
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void SetStepStatus(
        DebtorEnrichmentProfile enrichment,
        DebtEnrichmentStepKind kind,
        DebtEnrichmentStepStatus status)
    {
        switch (kind)
        {
            case DebtEnrichmentStepKind.Fns:
                enrichment.FnsStepStatus = status;
                break;
            case DebtEnrichmentStepKind.Bankruptcy:
                enrichment.BankruptcyStepStatus = status;
                break;
            case DebtEnrichmentStepKind.Kad:
                enrichment.KadStepStatus = status;
                break;
            case DebtEnrichmentStepKind.Fssp:
                enrichment.FsspStepStatus = status;
                break;
        }
    }
}
