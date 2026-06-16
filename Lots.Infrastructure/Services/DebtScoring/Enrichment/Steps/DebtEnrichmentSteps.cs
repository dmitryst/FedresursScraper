using Lots.Application.Services.DebtScoring;
using Lots.Application.Services.DebtScoring.Enrichment;
using Lots.Data.Entities.DebtScoring;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services.DebtScoring.Enrichment.Steps;

public class DadataEnrichmentStep : DebtEnrichmentStepBase
{
    public DadataEnrichmentStep(ILogger<DadataEnrichmentStep> logger) : base(logger)
    {
    }

    public override string StepName => "Dadata";

    public override DebtEnrichmentStepKind Kind => DebtEnrichmentStepKind.Fns;

    public override bool IsEnabled(DebtScoringOptions options) => options.EnableDadataStep;

    protected override Task<DebtEnrichmentStepResult> ExecuteCoreAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Dadata enrichment not implemented yet for lot {LotId}", context.Profile.LotId);
        return Task.FromResult(DebtEnrichmentStepResult.Skipped("Dadata integration pending (Phase 2.2)"));
    }
}

public class BankruptcyEnrichmentStep : DebtEnrichmentStepBase
{
    public BankruptcyEnrichmentStep(ILogger<BankruptcyEnrichmentStep> logger) : base(logger)
    {
    }

    public override string StepName => "Bankruptcy";

    public override DebtEnrichmentStepKind Kind => DebtEnrichmentStepKind.Bankruptcy;

    public override bool IsEnabled(DebtScoringOptions options) => options.EnableBankruptcyStep;

    protected override Task<DebtEnrichmentStepResult> ExecuteCoreAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Bankruptcy check not implemented yet for lot {LotId}", context.Profile.LotId);
        return Task.FromResult(DebtEnrichmentStepResult.Skipped("Bankruptcy check pending (Phase 2.3)"));
    }
}

public class KadEnrichmentStep : DebtEnrichmentStepBase
{
    public KadEnrichmentStep(ILogger<KadEnrichmentStep> logger) : base(logger)
    {
    }

    public override string StepName => "KAD";

    public override DebtEnrichmentStepKind Kind => DebtEnrichmentStepKind.Kad;

    public override bool IsEnabled(DebtScoringOptions options) => options.EnableKadStep;

    protected override Task<DebtEnrichmentStepResult> ExecuteCoreAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("KAD.arbitr enrichment not implemented yet for lot {LotId}", context.Profile.LotId);
        return Task.FromResult(DebtEnrichmentStepResult.Skipped("KAD.arbitr integration pending (Phase 2.4)"));
    }
}

public class FsspEnrichmentStep : DebtEnrichmentStepBase
{
    public FsspEnrichmentStep(ILogger<FsspEnrichmentStep> logger) : base(logger)
    {
    }

    public override string StepName => "FSSP";

    public override DebtEnrichmentStepKind Kind => DebtEnrichmentStepKind.Fssp;

    public override bool IsEnabled(DebtScoringOptions options) => options.EnableFsspStep;

    protected override Task<DebtEnrichmentStepResult> ExecuteCoreAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("FSSP enrichment not implemented yet for lot {LotId}", context.Profile.LotId);
        return Task.FromResult(DebtEnrichmentStepResult.Skipped("FSSP integration pending (Phase 2.5)"));
    }
}
