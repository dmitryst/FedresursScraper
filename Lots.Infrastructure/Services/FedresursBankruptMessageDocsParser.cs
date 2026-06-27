using System.Text.Json;
using FedresursScraper.Services.Models;

namespace FedresursScraper.Services;

public static class FedresursBankruptMessageDocsParser
{
    public const string MessageApiUrlTemplate = "https://fedresurs.ru/backend/bankruptcy-messages/{0}";
    public const string DocumentDownloadUrlTemplate = "https://fedresurs.ru/backend/bankruptcy-message-docs/{0}";

    public static string BuildMessageApiUrl(Guid messageId) =>
        string.Format(MessageApiUrlTemplate, messageId);

    public static string BuildDocumentDownloadUrl(Guid documentId) =>
        string.Format(DocumentDownloadUrlTemplate, documentId);

    public static List<FedresursAttachmentInfo> ParseAttachmentsFromApiJson(string json)
    {
        var attachments = new List<FedresursAttachmentInfo>();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("docs", out var docsElement) ||
            docsElement.ValueKind != JsonValueKind.Array)
        {
            return attachments;
        }

        foreach (var docElement in docsElement.EnumerateArray())
        {
            if (!docElement.TryGetProperty("guid", out var guidElement))
                continue;

            var guidRaw = guidElement.GetString();
            if (!Guid.TryParse(guidRaw, out var documentId))
                continue;

            var title = docElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var url = BuildDocumentDownloadUrl(documentId);
            var extension = LotPropertyDocumentHelper.DetectDocumentExtension(url, title);
            if (extension == null)
                continue;

            if (!LotPropertyDocumentHelper.IsLikelyTradeDocumentLink(title, url, extension))
                continue;

            attachments.Add(new FedresursAttachmentInfo
            {
                Title = title,
                Url = url,
                Extension = extension,
            });
        }

        return attachments;
    }
}
