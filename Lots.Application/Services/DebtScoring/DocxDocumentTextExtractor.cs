using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Lots.Application.Services.DebtScoring;

public class DocxDocumentTextExtractor : IDocumentTextExtractor
{
    public bool CanExtract(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentTextExtractionResult> ExtractAsync(
        byte[] fileContent,
        string extension,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var document = WordprocessingDocument.Open(stream, false);

            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
            {
                return Task.FromResult(DocumentTextExtractionResult.Fail("Пустой docx-документ"));
            }

            var paragraphs = body.Descendants<Paragraph>()
                .Select(p => p.InnerText?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t));

            var text = string.Join(Environment.NewLine, paragraphs);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(DocumentTextExtractionResult.Fail("Текст в docx не найден"));
            }

            return Task.FromResult(DocumentTextExtractionResult.Ok(text, confidence: 1.0));
        }
        catch (Exception ex)
        {
            return Task.FromResult(DocumentTextExtractionResult.Fail($"Ошибка чтения docx: {ex.Message}"));
        }
    }
}
