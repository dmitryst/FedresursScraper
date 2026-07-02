namespace Lots.Application.Interfaces;

public class LotDescriptionSplitResult
{
    public string Description { get; set; } = string.Empty;
    public string ViewingProcedure { get; set; } = string.Empty;
    public bool UsedLlm { get; set; }
}

public interface ILotDescriptionSplitter
{
    Task<LotDescriptionSplitResult> SplitAsync(string rawText, CancellationToken cancellationToken = default);
}