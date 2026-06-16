namespace Lots.Application.Services.DebtScoring;

public interface IDocumentTextExtractor
{
    bool CanExtract(string extension);

    Task<DocumentTextExtractionResult> ExtractAsync(byte[] fileContent, string extension, CancellationToken cancellationToken = default);
}

public sealed class DocumentTextExtractionResult
{
    public bool Success { get; init; }

    public string? Text { get; init; }

    public double? Confidence { get; init; }

    public string? Error { get; init; }

    public static DocumentTextExtractionResult Ok(string text, double? confidence = null) =>
        new() { Success = true, Text = text, Confidence = confidence };

    public static DocumentTextExtractionResult Fail(string error) =>
        new() { Success = false, Error = error };
}
