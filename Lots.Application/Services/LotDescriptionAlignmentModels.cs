namespace FedresursScraper.Services;

public class LotDescriptionAlignmentPreviewDto
{
    public int PublicId { get; set; }
    public Guid LotId { get; set; }
    public string? LotNumber { get; set; }
    public decimal? StartPrice { get; set; }
    public string? FedresursUrl { get; set; }
    public string? Error { get; set; }

    public string? CurrentDescription { get; set; }
    public string? CurrentViewingProcedure { get; set; }

    public string? ProposedDescription { get; set; }
    public string? ProposedViewingProcedure { get; set; }

    public bool CanApply =>
        string.IsNullOrEmpty(Error) &&
        !string.IsNullOrWhiteSpace(ProposedDescription) &&
        !string.Equals(ProposedDescription.Trim(), CurrentDescription?.Trim(), StringComparison.Ordinal);
}

public class ApplyLotDescriptionAlignmentRequest
{
    public int PublicId { get; set; }
    public string Description { get; set; } = default!;
    public string? ViewingProcedure { get; set; }
}
