namespace Lots.Application.Interfaces;

public class PropertyDescriptionSummaryResult
{
    public string? Summary { get; set; }
    public string? Error { get; set; }
    public bool IsSummarized { get; set; }
}

public interface ILotPropertyDescriptionSummarizer
{
    Task<PropertyDescriptionSummaryResult> SummarizeAsync(
        string rawText,
        CancellationToken cancellationToken = default);
}
