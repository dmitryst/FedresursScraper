using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace FedresursScraper.Services.Enrichments;

/// <summary>
/// Чистый HTML-парсер страниц Альфалот (без I/O).
/// </summary>
public static class AlfalotHtmlParser
{
    public const string BaseUrl = "https://bankrupt.alfalot.ru";
    public const string PurchasesAllUrl = BaseUrl + "/public/purchases-all/";

    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Окончен",
        "Не состоялся",
        "Отменён организатором"
    };

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    public sealed class CatalogRow
    {
        public string TradeNumber { get; init; } = "";
        public string LotNumber { get; init; } = "";
        public string TradeUrl { get; init; } = "";
        public string LotUrl { get; init; } = "";
        public string? Status { get; init; }
        public DateTime? ApplicationsEndAt { get; init; }
        public DateTime? EventAt { get; init; }
    }

    public sealed class PriceScheduleRow
    {
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public decimal Price { get; init; }
        public decimal Deposit { get; init; }
    }

    public static bool IsWafChallenge(string html)
    {
        if (string.IsNullOrEmpty(html))
            return false;

        return html.Contains("inprotect_", StringComparison.OrdinalIgnoreCase)
               || html.Contains("inprotect_ok_", StringComparison.OrdinalIgnoreCase)
               || html.Contains("canvasFP", StringComparison.Ordinal);
    }

    public static string NormalizeTradeNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
            return value.Trim().ToLowerInvariant();

        var normalized = digits.TrimStart('0');
        return string.IsNullOrEmpty(normalized) ? "0" : normalized;
    }

    public static string NormalizeLotNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (trimmed.All(char.IsDigit))
        {
            var normalized = trimmed.TrimStart('0');
            return string.IsNullOrEmpty(normalized) ? "0" : normalized;
        }

        return trimmed.ToLowerInvariant();
    }

    public static string ToAbsoluteUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return string.Empty;

        href = WebUtility.HtmlDecode(href).Trim();

        // Wayback / absolute already
        var bankruptIdx = href.IndexOf("bankrupt.alfalot.ru", StringComparison.OrdinalIgnoreCase);
        if (bankruptIdx >= 0)
        {
            var pathStart = href.IndexOf('/', bankruptIdx + "bankrupt.alfalot.ru".Length);
            if (pathStart >= 0)
                href = href[pathStart..];
            else
                return BaseUrl + "/";
        }

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href.Split('#')[0].TrimEnd('/');

        if (!href.StartsWith('/'))
            href = "/" + href;

        return (BaseUrl + href.Split('#')[0]).TrimEnd('/');
    }

    public static bool IsFinishedStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var clean = CleanText(status);
        return FinishedStatuses.Any(s => clean.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    public static List<CatalogRow> ParseCatalogRows(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = new List<CatalogRow>();
        var trNodes = doc.DocumentNode.SelectNodes("//tr[contains(@class,'gridRow') or contains(@class,'gridAltRow')]");
        if (trNodes == null)
            return rows;

        foreach (var tr in trNodes)
        {
            var tds = tr.SelectNodes("./td");
            if (tds == null || tds.Count < 4)
                continue;

            var tradeLink = tds[0].SelectSingleNode(".//a[@href]");
            var lotNumberLink = tds[2].SelectSingleNode(".//a[@href]");
            if (tradeLink == null || lotNumberLink == null)
                continue;

            var tradeHref = tradeLink.GetAttributeValue("href", "");
            var lotHref = lotNumberLink.GetAttributeValue("href", "");
            if (!IsTradeOrLotHref(tradeHref) || !IsLotHref(lotHref))
                continue;

            var tradeNumber = CleanText(tradeLink.InnerText);
            var lotNumber = CleanText(lotNumberLink.InnerText);
            if (string.IsNullOrWhiteSpace(tradeNumber) || string.IsNullOrWhiteSpace(lotNumber))
                continue;

            string? status = null;
            DateTime? applicationsEndAt = null;
            DateTime? eventAt = null;

            if (tds.Count > 6)
                applicationsEndAt = TryParseMoscowDate(CleanText(tds[6].InnerText));
            if (tds.Count > 7)
                eventAt = TryParseMoscowDate(CleanText(tds[7].InnerText));
            if (tds.Count > 8)
                status = CleanText(tds[8].InnerText);

            rows.Add(new CatalogRow
            {
                TradeNumber = tradeNumber,
                LotNumber = lotNumber,
                TradeUrl = ToAbsoluteUrl(tradeHref) + "/",
                LotUrl = ToAbsoluteUrl(lotHref) + "/",
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                ApplicationsEndAt = applicationsEndAt,
                EventAt = eventAt
            });
        }

        return rows;
    }

    public static Dictionary<string, string> ExtractAspNetFormFields(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var inputs = doc.DocumentNode.SelectNodes("//input[@name]");
        if (inputs != null)
        {
            foreach (var input in inputs)
            {
                var name = input.GetAttributeValue("name", "");
                if (string.IsNullOrEmpty(name))
                    continue;

                var type = input.GetAttributeValue("type", "text");
                if (type.Equals("submit", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("button", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result[name] = input.GetAttributeValue("value", "");
            }
        }

        var textareas = doc.DocumentNode.SelectNodes("//textarea[@name]");
        if (textareas != null)
        {
            foreach (var ta in textareas)
            {
                var name = ta.GetAttributeValue("name", "");
                if (!string.IsNullOrEmpty(name))
                    result[name] = ta.InnerText ?? "";
            }
        }

        var selects = doc.DocumentNode.SelectNodes("//select[@name]");
        if (selects != null)
        {
            foreach (var select in selects)
            {
                var name = select.GetAttributeValue("name", "");
                if (string.IsNullOrEmpty(name))
                    continue;

                var selected = select.SelectSingleNode(".//option[@selected]")
                               ?? select.SelectSingleNode(".//option");
                result[name] = selected?.GetAttributeValue("value", "") ?? "";
            }
        }

        return result;
    }

    public static List<(string EventTarget, int PageNumber)> ExtractPagerTargets(string html)
    {
        var result = new List<(string, int)>();
        var matches = Regex.Matches(
            html,
            @"__doPostBack\('([^']*PurchasesSearchResult[^']*)'\s*,\s*''\)[^>]*>\s*(\d+)\s*<",
            RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[2].Value, out var page))
                result.Add((match.Groups[1].Value, page));
        }

        return result
            .GroupBy(x => x.Item2)
            .Select(g => g.First())
            .OrderBy(x => x.Item2)
            .ToList();
    }

    public static int? ExtractCurrentPageNumber(string html)
    {
        var match = Regex.Match(
            html,
            @"class=['""]pager['""][\s\S]*?<span>(\d+)</span>",
            RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var page))
            return page;

        return null;
    }

    public static List<string> ExtractImageUrls(string html)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // 1) prettyPhoto gallery
        var prettyNodes = doc.DocumentNode.SelectNodes("//a[starts-with(@rel,'prettyPhoto')]");
        if (prettyNodes != null)
        {
            foreach (var node in prettyNodes)
            {
                var href = node.GetAttributeValue("href", "");
                if (IsImageHref(href))
                    urls.Add(ToAbsoluteUrl(href));
            }
        }

        // 2) attachments table — image files or type "Изображение"
        var attachmentRows = doc.DocumentNode.SelectNodes(
            "//tr[contains(@class,'attachment-grid-row')]");
        if (attachmentRows != null)
        {
            foreach (var row in attachmentRows)
            {
                var link = row.SelectSingleNode(".//a[contains(@href,'/attachments/file/')]");
                if (link == null)
                    continue;

                var href = link.GetAttributeValue("href", "");
                var fileName = CleanText(link.InnerText);
                var typeText = CleanText(row.InnerText);

                if (IsImageHref(href) || IsImageHref(fileName)
                    || typeText.Contains("Изображен", StringComparison.OrdinalIgnoreCase))
                {
                    urls.Add(ToAbsoluteUrl(href));
                }
            }
        }

        // 3) fallback: any attachment file with image extension
        if (urls.Count == 0)
        {
            var anyLinks = doc.DocumentNode.SelectNodes("//a[contains(@href,'/attachments/file/')]");
            if (anyLinks != null)
            {
                foreach (var link in anyLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (IsImageHref(href) || IsImageHref(CleanText(link.InnerText)))
                        urls.Add(ToAbsoluteUrl(href));
                }
            }
        }

        return urls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
    }

    public static List<PriceScheduleRow> ExtractPriceSchedule(string html)
    {
        var result = new List<PriceScheduleRow>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Предпочитаем блок публичного предложения
        var table = doc.DocumentNode.SelectSingleNode(
            "//*[contains(@id,'PublicOfferReduction') or contains(@id,'publicOfferReduction')]//table[.//th or .//td[contains(@class,'gridHeader') or contains(.,'Дата')]]");

        table ??= doc.DocumentNode.SelectSingleNode(
            "//table[.//text()[contains(.,'Цена на интервале')] and .//text()[contains(.,'Дата начала')]]");

        if (table == null)
            return result;

        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2)
            return result;

        var headerRow = rows[0];
        var headerCells = headerRow.SelectNodes("./th|./td");
        if (headerCells == null || headerCells.Count == 0)
            return result;

        var headers = headerCells.Select(c => CleanText(c.InnerText)).ToList();
        var startIdx = FindHeaderIndex(headers, "Дата начала приема заявок", "Дата начала интервала");
        var endIdx = FindHeaderIndex(headers, "Дата окончания приема заявок", "Дата окончания интервала");
        var priceIdx = FindHeaderIndex(headers, "Цена на интервале");
        var depositIdx = FindHeaderIndex(headers, "Задаток на интервале", "Задаток");

        if (startIdx < 0 || endIdx < 0 || priceIdx < 0)
            return result;

        foreach (var row in rows.Skip(1))
        {
            var cols = row.SelectNodes("./td");
            if (cols == null || cols.Count <= Math.Max(startIdx, Math.Max(endIdx, priceIdx)))
                continue;

            if (!TryParseMoscowDate(CleanText(cols[startIdx].InnerText), out var start)
                || !TryParseMoscowDate(CleanText(cols[endIdx].InnerText), out var end)
                || !TryParsePrice(cols[priceIdx].InnerText, out var price))
            {
                continue;
            }

            decimal deposit = 0;
            if (depositIdx >= 0 && depositIdx < cols.Count)
                TryParsePrice(cols[depositIdx].InnerText, out deposit);

            result.Add(new PriceScheduleRow
            {
                StartDate = start,
                EndDate = end,
                Price = price,
                Deposit = deposit
            });
        }

        return result;
    }

    private static int FindHeaderIndex(List<string> headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            foreach (var candidate in candidates)
            {
                if (headers[i].Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return -1;
    }

    private static bool IsTradeOrLotHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;

        return href.Contains("/auctions/view/", StringComparison.OrdinalIgnoreCase)
               || href.Contains("/public-offers/view/", StringComparison.OrdinalIgnoreCase)
               || href.Contains("/contests/view/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLotHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;

        return href.Contains("/lots/view/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var clean = href.Split('?', '#')[0];
        return ImageExtensions.Any(ext => clean.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static string CleanText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var text = WebUtility.HtmlDecode(input)
            .Replace('\u00A0', ' ')
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static bool TryParsePrice(string? rawText, out decimal price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        var clean = CleanText(rawText)
            .Replace(" ", "")
            .Replace("руб.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("руб", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("ru-RU"), out price);
    }

    public static DateTime? TryParseMoscowDate(string? raw)
    {
        return TryParseMoscowDate(raw, out var dt) ? dt : null;
    }

    public static bool TryParseMoscowDate(string? raw, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var clean = CleanText(raw);
        var formats = new[]
        {
            "dd.MM.yyyy HH:mm",
            "dd.MM.yyyy H:mm",
            "dd.MM.yyyy"
        };

        if (!DateTime.TryParseExact(clean, formats, new CultureInfo("ru-RU"), DateTimeStyles.None, out var local)
            && !DateTime.TryParse(clean, new CultureInfo("ru-RU"), DateTimeStyles.None, out local))
        {
            return false;
        }

        TimeZoneInfo moscow;
        try
        {
            moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException)
        {
            moscow = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), moscow);
        return true;
    }
}
