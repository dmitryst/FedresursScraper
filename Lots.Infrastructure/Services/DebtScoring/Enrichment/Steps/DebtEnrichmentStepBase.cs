using Lots.Application.Services.DebtScoring;
using Lots.Application.Services.DebtScoring.Enrichment;
using Lots.Data.Entities.DebtScoring;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services.DebtScoring.Enrichment.Steps;

public abstract class DebtEnrichmentStepBase : IDebtEnrichmentStep
{
    protected DebtEnrichmentStepBase(ILogger logger)
    {
        Logger = logger;
    }

    protected ILogger Logger { get; }

    public abstract string StepName { get; }

    public abstract DebtEnrichmentStepKind Kind { get; }

    public abstract bool IsEnabled(DebtScoringOptions options);

    public Task<DebtEnrichmentStepResult> ExecuteAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(context.Options))
        {
            Logger.LogDebug("{Step} skipped for lot {LotId}: disabled in config", StepName, context.Profile.LotId);
            return Task.FromResult(DebtEnrichmentStepResult.Skipped("Step disabled in configuration"));
        }

        return ExecuteCoreAsync(context, cancellationToken);
    }

    protected abstract Task<DebtEnrichmentStepResult> ExecuteCoreAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken);
}
