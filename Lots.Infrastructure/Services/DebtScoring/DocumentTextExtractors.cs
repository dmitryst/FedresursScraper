using Lots.Application.Services.DebtScoring;

namespace FedresursScraper.Services.DebtScoring;

public class OcrDocumentTextExtractor : IDocumentTextExtractor
{
    private readonly IOcrServiceClient _ocrClient;

    public OcrDocumentTextExtractor(IOcrServiceClient ocrClient)
    {
        _ocrClient = ocrClient;
    }

    public bool CanExtract(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase);

    public async Task<DocumentTextExtractionResult> ExtractAsync(
        byte[] fileContent,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"document{extension}";
        var result = await _ocrClient.RecognizeAsync(fileContent, fileName, cancellationToken);

        if (!result.Success)
        {
            return DocumentTextExtractionResult.Fail(result.Error ?? "OCR failed");
        }

        return DocumentTextExtractionResult.Ok(result.Text!, result.Confidence);
    }
}

public class CompositeDocumentTextExtractor : IDocumentTextExtractor
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;

    public CompositeDocumentTextExtractor(IEnumerable<IDocumentTextExtractor> extractors)
    {
        _extractors = extractors;
    }

    public bool CanExtract(string extension) =>
        _extractors.Any(e => e.CanExtract(extension));

    public async Task<DocumentTextExtractionResult> ExtractAsync(
        byte[] fileContent,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(normalized));

        if (extractor == null)
        {
            return DocumentTextExtractionResult.Fail($"Нет экстрактора для {normalized}");
        }

        return await extractor.ExtractAsync(fileContent, normalized, cancellationToken);
    }
}
