using Lots.Data.Entities;
using Lots.Data.Entities.DebtScoring;

namespace Lots.Application.Services.DebtScoring.Enrichment;

public sealed class DebtEnrichmentContext
{
    public required DebtLotProfile Profile { get; init; }

    public required DebtorEnrichmentProfile Enrichment { get; init; }

    public required DebtScoringOptions Options { get; init; }

    public Subject? Debtor { get; init; }

    public string? ResolvedInn { get; set; }

    public string? ResolvedName { get; set; }

    public string? ResolvedSnils { get; set; }

    public string? CaseNumber { get; set; }
}

public sealed class DebtEnrichmentStepResult
{
    public DebtEnrichmentStepStatus Status { get; init; }

    public string? Error { get; init; }

    public static DebtEnrichmentStepResult Completed() =>
        new() { Status = DebtEnrichmentStepStatus.Completed };

    public static DebtEnrichmentStepResult Skipped(string? reason = null) =>
        new() { Status = DebtEnrichmentStepStatus.Skipped, Error = reason };

    public static DebtEnrichmentStepResult Failed(string error) =>
        new() { Status = DebtEnrichmentStepStatus.Failed, Error = error };
}

public interface IDebtEnrichmentStep
{
    string StepName { get; }

    DebtEnrichmentStepKind Kind { get; }

    bool IsEnabled(DebtScoringOptions options);

    Task<DebtEnrichmentStepResult> ExecuteAsync(
        DebtEnrichmentContext context,
        CancellationToken cancellationToken);
}

public enum DebtEnrichmentStepKind
{
    Fns = 0,
    Bankruptcy = 1,
    Kad = 2,
    Fssp = 3,
}
