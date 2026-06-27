namespace FedresursScraper.Services;

public static class LotDocumentLinkHelper
{
    public static string BuildDownloadApiPath(int lotPublicId, Guid documentId) =>
        $"/api/lots/{lotPublicId}/documents/{documentId}/download";

    public static bool IsFedresursDocumentUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.Contains("fedresurs.ru/backend/bankruptcy-message-docs/", StringComparison.OrdinalIgnoreCase);
}
