using Lots.Data.Entities.DebtScoring;

namespace Lots.Application.Services.DebtScoring.Models;

public sealed class ExtractedEntityResult
{
    public ExtractedEntityType EntityType { get; init; }

    public string Value { get; init; } = default!;

    public double Confidence { get; init; }

    public EntityExtractionSource Source { get; init; } = EntityExtractionSource.Regex;
}

public sealed class CourtActExtractionResult
{
    public IReadOnlyList<ExtractedEntityResult> Entities { get; init; } = Array.Empty<ExtractedEntityResult>();

    public decimal? DebtNominal { get; init; }
}
