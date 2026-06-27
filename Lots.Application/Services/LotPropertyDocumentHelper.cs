using FedresursScraper.Services.Models;

namespace FedresursScraper.Services;

public static class LotPropertyDocumentHelper
{
    public static readonly string[] PropertyDocumentExtensions = [".docx", ".doc", ".pdf", ".xlsx", ".xls", ".rtf", ".rar", ".zip"];

    private static readonly string[] JunkAttachmentKeywords =
    [
        "интерфакс",
        "персональн",
        "политика ао",
        "обработки и защиты персональных",
        "privacy",
        "cookie",
        "пользовательское соглашение",
    ];

    private static readonly string[] PropertyDocumentTitleKeywords =
    [
        "состав лота",
        "перечень имущества",
        "описание имущества",
        "описание лота",
        "имущество",
    ];

    private static readonly string[] ExcludedAttachmentKeywords =
    [
        "договор",
        "задат",
        "купли-продаж",
        "купли продаж",
        "оферт",
        "положение о торгах",
        "извещение",
    ];

    public static bool IsContractOrTemplateAttachment(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return ExcludedAttachmentKeywords.Any(k =>
            title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsJunkAttachment(string? title, string? url)
    {
        var haystack = $"{title} {url}";
        if (string.IsNullOrWhiteSpace(haystack))
            return true;

        if (JunkAttachmentKeywords.Any(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrWhiteSpace(url) &&
            !url.Contains("fedresurs.ru", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsLikelyTradeDocumentLink(string? title, string? url, string? extension)
    {
        if (IsJunkAttachment(title, url))
            return false;

        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return PropertyDocumentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static string? DetectDocumentExtension(string href, string? title)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        foreach (var ext in PropertyDocumentExtensions)
        {
            if (href.Contains(ext, StringComparison.OrdinalIgnoreCase))
                return ext;

            if (!string.IsNullOrWhiteSpace(title) && title.Contains(ext, StringComparison.OrdinalIgnoreCase))
                return ext;
        }

        return null;
    }

    public static bool IsPropertyDocumentAttachment(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return PropertyDocumentTitleKeywords.Any(k =>
            title.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static bool GetDefaultSelectedForDownload(string? title) =>
        !IsContractOrTemplateAttachment(title);

    public static bool GetDefaultUseForDescription(string? title, bool hasExtractedText) =>
        hasExtractedText
        && IsPropertyDocumentAttachment(title)
        && !IsContractOrTemplateAttachment(title);

    public const int SummarizeThresholdChars = 1500;
    public const int SummarizeThresholdLines = 25;

    public static bool NeedsSummarization(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Length >= SummarizeThresholdChars)
            return true;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length >= SummarizeThresholdLines)
            return true;

        var numberedLines = lines.Count(l =>
            System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+[\.\)\-]\s"));

        return numberedLines >= 10;
    }

    public static string PrepareTextForSummarization(string text, int maxChars = 14000)
    {
        if (text.Length <= maxChars)
            return text;

        var headLength = (int)(maxChars * 0.55);
        var tailLength = (int)(maxChars * 0.35);
        var head = text[..headLength];
        var tail = text[^tailLength..];

        return head
               + "\n\n[... средняя часть документа опущена ...]\n\n"
               + tail
               + $"\n\n[Полный документ: {text.Length} символов. Обобщи по фрагменту.]";
    }

    public static string TruncateForPreview(string? text, int maxChars = 600)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : $"{trimmed[..maxChars]}…";
    }

    public static string? ResolveDescriptionText(string? rawText, string? summarizedText, bool isSummarized)
    {
        if (isSummarized && !string.IsNullOrWhiteSpace(summarizedText))
            return summarizedText.Trim();

        return string.IsNullOrWhiteSpace(rawText) ? null : rawText.Trim();
    }

    public static string? MergeExtractedTexts(IEnumerable<string?> texts)
    {
        var parts = texts
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    public static string? BuildProposedDescription(string? tableDescription, IEnumerable<string?> documentTexts)
    {
        var documentText = MergeExtractedTexts(documentTexts);
        return BuildProposedDescription(tableDescription, documentText);
    }

    public static bool IsPropertyListReferral(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("Полный перечень имущества", StringComparison.OrdinalIgnoreCase)
               || text.Contains("перечень имущества опубликован", StringComparison.OrdinalIgnoreCase)
               || text.Contains("опубликован на сайте ЕФРСБ", StringComparison.OrdinalIgnoreCase)
               || text.Contains("опубликован на сайте электронной", StringComparison.OrdinalIgnoreCase)
               || text.Contains("на сайте электронной площадки", StringComparison.OrdinalIgnoreCase);
    }

    public static string? BuildProposedDescription(string? tableDescription, string? documentText)
    {
        var table = tableDescription?.Trim() ?? string.Empty;
        var doc = documentText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(doc))
            return string.IsNullOrWhiteSpace(table) ? null : table;

        if (IsPropertyListReferral(table) || string.IsNullOrWhiteSpace(table) ||
            table.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
        {
            return doc;
        }

        if (doc.Length > table.Length * 1.5)
            return doc;

        return table;
    }

    public static IReadOnlyList<FedresursAttachmentInfo> SelectAttachmentsForLot(
        IReadOnlyList<FedresursAttachmentInfo> attachments,
        string? lotNumber)
    {
        if (attachments.Count == 0)
            return [];

        if (attachments.Count == 1)
            return attachments;

        var normalizedLot = NormalizeLotNumber(lotNumber);
        if (!string.IsNullOrEmpty(normalizedLot))
        {
            var byLotNumber = attachments
                .Where(a => TitleOrUrlContainsLotNumber(a, normalizedLot))
                .ToList();
            if (byLotNumber.Count > 0)
                return byLotNumber;
        }

        var byKeyword = attachments
            .Where(a => PropertyDocumentTitleKeywords.Any(k =>
                a.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (byKeyword.Count > 0)
            return byKeyword;

        return attachments;
    }

    public static string GetContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".rtf" => "application/rtf",
            ".rar" => "application/vnd.rar",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };

    private static bool TitleOrUrlContainsLotNumber(FedresursAttachmentInfo attachment, string lotNumber)
    {
        var haystack = $"{attachment.Title} {attachment.Url}";
        return haystack.Contains($"лот {lotNumber}", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains($"лот №{lotNumber}", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains($"лот{lotNumber}", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains($"№{lotNumber}", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains($"_{lotNumber}.", StringComparison.OrdinalIgnoreCase)
               || haystack.Contains($"-{lotNumber}.", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
            return string.Empty;

        return System.Text.RegularExpressions.Regex
            .Replace(lotNumber.Trim(), @"(?i)^лот\s*№?\s*", "")
            .Trim();
    }
}
