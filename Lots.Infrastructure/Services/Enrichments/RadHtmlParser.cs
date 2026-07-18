using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace FedresursScraper.Services.Enrichments;

/// <summary>
/// Чистый HTML-парсер страниц РАД / catalog.lot-online.ru (без I/O).
/// </summary>
public static class RadHtmlParser
{
    public const string BaseUrl = "https://catalog.lot-online.ru";

    /// <summary>
    /// Каталог «Имущество должников», сортировка от новых к старым, короткий список.
    /// </summary>
    public const string DebtorCatalogPath =
        "/index.php?dispatch=categories.view&category_id=9876&features_hash=172-186359" +
        "&sort_by=timestamp&sort_order=desc&layout=short_list";

    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Завершена",
        "Не состоялась",
        "Отменена",
        "Приостановлена"
    };

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];

    private static readonly Regex ProductIdRegex = new(
        @"product_id=(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LotNumberInTitleRegex = new(
        @"Лот\s*№\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RadLotCodeRegex = new(
        @"РАД-\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LotUnidRegex = new(
        @"lotUnid%3D(\d+)|lotUnid=(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EfrsbLotIdRegex = new(
        @"Идентификатор\s+лота\s+в\s+ЕФРСБ\s*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed class CatalogItem
    {
        public long ProductId { get; init; }
        public string LotUrl { get; init; } = "";
        public string? Title { get; init; }
        public string? LotNumber { get; init; }
        public string? LotCode { get; init; }
        public string? Status { get; init; }
    }

    public sealed class PriceScheduleRow
    {
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public decimal Price { get; init; }
        public decimal Deposit { get; init; }
    }

    public static string BuildCatalogPageUrl(int page)
    {
        page = Math.Max(1, page);
        return $"{BaseUrl}{DebtorCatalogPath}&page={page}";
    }

    public static string BuildProductUrl(long productId) =>
        $"{BaseUrl}/index.php?dispatch=products.view&product_id={productId}";

    public static string BuildEAuctionUrl(string lotUnid) =>
        $"{BaseUrl}/e-auction/auctionLotProperty.v.xhtml?parm=lotUnid%3D{Uri.EscapeDataString(lotUnid)}%3Bmode%3Djust";

    public static string NormalizeEfrsbLotId(string? value) =>
        AlfalotHtmlParser.NormalizeTradeNumber(value);

    public static string NormalizeLotNumber(string? value) =>
        AlfalotHtmlParser.NormalizeLotNumber(value);

    public static bool IsFinishedStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var clean = CleanText(status);
        return FinishedStatuses.Any(s => clean.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    public static string ToAbsoluteUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return string.Empty;

        href = WebUtility.HtmlDecode(href).Trim().Split('#')[0];
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        if (!href.StartsWith('/'))
            href = "/" + href;

        return BaseUrl + href;
    }

    public static List<CatalogItem> ParseCatalogItems(string html)
    {
        var result = new List<CatalogItem>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var blocks = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'ty-compact-list__item') or contains(@class,'ty-compact-list__content')]");
        if (blocks == null || blocks.Count == 0)
        {
            // Fallback: собираем уникальные product_id из ссылок
            return ParseCatalogItemsFromLinks(html);
        }

        var seen = new HashSet<long>();
        foreach (var block in blocks)
        {
            var link = block.SelectSingleNode(".//a[contains(@href,'dispatch=products.view') and contains(@href,'product_id=')]")
                       ?? block.SelectSingleNode(".//a[contains(@class,'product-title')]");
            if (link == null)
                continue;

            var href = link.GetAttributeValue("href", "");
            var productId = TryExtractProductId(href);
            if (productId == null || !seen.Add(productId.Value))
                continue;

            var title = CleanText(link.GetAttributeValue("title", ""));
            if (string.IsNullOrWhiteSpace(title))
                title = CleanText(link.InnerText);
            if (string.IsNullOrWhiteSpace(title))
                title = null;

            var blockText = CleanText(block.InnerText);
            var lotCode = RadLotCodeRegex.Match(blockText) is { Success: true } codeMatch
                ? codeMatch.Value.ToUpperInvariant()
                : null;

            string? lotNumber = null;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var lotMatch = LotNumberInTitleRegex.Match(title);
                if (lotMatch.Success)
                    lotNumber = lotMatch.Groups[1].Value;
            }

            string? status = null;
            var statusNode = block.SelectSingleNode(
                ".//*[contains(@class,'list-lot-info-status') or contains(@class,'lot-info-status')]");
            if (statusNode != null)
                status = CleanText(statusNode.InnerText);

            if (string.IsNullOrWhiteSpace(status) && !string.IsNullOrWhiteSpace(blockText))
            {
                foreach (var candidate in new[]
                         {
                             "Идет прием заявок", "Опубликована", "Завершена", "Не состоялась",
                             "Отменена", "Приостановлена", "Подача предложений", "Подведение итогов"
                         })
                {
                    if (blockText.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        status = candidate;
                        break;
                    }
                }
            }

            result.Add(new CatalogItem
            {
                ProductId = productId.Value,
                LotUrl = BuildProductUrl(productId.Value),
                Title = title,
                LotNumber = lotNumber,
                LotCode = lotCode,
                Status = status
            });
        }

        return result.Count > 0 ? result : ParseCatalogItemsFromLinks(html);
    }

    private static List<CatalogItem> ParseCatalogItemsFromLinks(string html)
    {
        var result = new List<CatalogItem>();
        var seen = new HashSet<long>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'dispatch=products.view') and contains(@href,'product_id=')]");
        if (links == null)
            return result;

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            var productId = TryExtractProductId(href);
            if (productId == null || !seen.Add(productId.Value))
                continue;

            var title = CleanText(link.GetAttributeValue("title", ""))
                        ?? CleanText(link.InnerText);
            string? lotNumber = null;
            if (!string.IsNullOrWhiteSpace(title))
            {
                var lotMatch = LotNumberInTitleRegex.Match(title);
                if (lotMatch.Success)
                    lotNumber = lotMatch.Groups[1].Value;
            }

            result.Add(new CatalogItem
            {
                ProductId = productId.Value,
                LotUrl = BuildProductUrl(productId.Value),
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                LotNumber = lotNumber
            });
        }

        return result;
    }

    public static long? TryExtractProductId(string? hrefOrText)
    {
        if (string.IsNullOrWhiteSpace(hrefOrText))
            return null;

        var match = ProductIdRegex.Match(WebUtility.HtmlDecode(hrefOrText));
        if (!match.Success)
            return null;

        return long.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    public static string? ExtractLotUnid(string productHtml)
    {
        if (string.IsNullOrWhiteSpace(productHtml))
            return null;

        var match = LotUnidRegex.Match(productHtml);
        if (!match.Success)
            return null;

        return match.Groups[1].Success && !string.IsNullOrEmpty(match.Groups[1].Value)
            ? match.Groups[1].Value
            : match.Groups[2].Value;
    }

    public static string? ExtractEfrsbLotId(string eAuctionHtml)
    {
        if (string.IsNullOrWhiteSpace(eAuctionHtml))
            return null;

        var text = StripTags(eAuctionHtml);
        var match = EfrsbLotIdRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ExtractLotNumber(string? title, string? productHtml = null)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var m = LotNumberInTitleRegex.Match(title);
            if (m.Success)
                return m.Groups[1].Value;
        }

        if (!string.IsNullOrWhiteSpace(productHtml))
        {
            var h1 = Regex.Match(
                productHtml,
                @"<h1[^>]*>(.*?)</h1>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (h1.Success)
            {
                var h1Text = CleanText(h1.Groups[1].Value);
                var m = LotNumberInTitleRegex.Match(h1Text);
                if (m.Success)
                    return m.Groups[1].Value;
            }

            var titleAttr = Regex.Match(
                productHtml,
                @"title=""([^""]*Лот\s*№\s*\d+[^""]*)""",
                RegexOptions.IgnoreCase);
            if (titleAttr.Success)
            {
                var m = LotNumberInTitleRegex.Match(WebUtility.HtmlDecode(titleAttr.Groups[1].Value));
                if (m.Success)
                    return m.Groups[1].Value;
            }
        }

        return null;
    }

    public static string? ExtractLotCode(string productHtml)
    {
        if (string.IsNullOrWhiteSpace(productHtml))
            return null;

        var text = StripTags(productHtml);
        // Предпочитаем код рядом с «Код лота»
        var near = Regex.Match(
            text,
            @"Код\s+лота\s*(РАД-\d+)",
            RegexOptions.IgnoreCase);
        if (near.Success)
            return near.Groups[1].Value.ToUpperInvariant();

        var any = RadLotCodeRegex.Match(text);
        return any.Success ? any.Value.ToUpperInvariant() : null;
    }

    public static List<string> ExtractImageUrls(string html)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var previewers = doc.DocumentNode.SelectNodes("//a[contains(@class,'cm-image-previewer')]");
        if (previewers != null)
        {
            foreach (var node in previewers)
            {
                var href = node.GetAttributeValue("href", "");
                if (IsLikelyLotImage(href))
                    urls.Add(ToAbsoluteUrl(href));
            }
        }

        if (urls.Count == 0)
        {
            var imgs = doc.DocumentNode.SelectNodes(
                "//*[contains(@class,'ty-product-img') or contains(@class,'ty-pict')]//img[@src]");
            if (imgs != null)
            {
                foreach (var img in imgs)
                {
                    var src = img.GetAttributeValue("src", "");
                    if (IsLikelyLotImage(src))
                        urls.Add(ToAbsoluteUrl(src));
                }
            }
        }

        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Where(u => !u.Contains("/images/detailed/534/", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static List<PriceScheduleRow> ExtractPriceSchedule(string html)
    {
        var result = new List<PriceScheduleRow>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        HtmlNode? table = null;
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables != null)
        {
            foreach (var candidate in tables)
            {
                var text = CleanText(candidate.InnerText);
                if (text.Contains("Время начала периода", StringComparison.OrdinalIgnoreCase)
                    || (text.Contains("Время окончания приема заявок", StringComparison.OrdinalIgnoreCase)
                        && text.Contains("Предложение", StringComparison.OrdinalIgnoreCase)))
                {
                    table = candidate;
                    break;
                }
            }
        }

        if (table == null)
            return result;

        var rows = table.SelectNodes(".//tr");
        if (rows == null || rows.Count < 2)
            return result;

        foreach (var row in rows)
        {
            var cols = row.SelectNodes("./td");
            if (cols == null || cols.Count < 6)
                continue;

            // Колонки: начало периода | окончание приема | окончание периода | изменение | предложение | задаток | ...
            if (!TryParseMoscowDate(CleanText(cols[0].InnerText), out var start)
                || !TryParseMoscowDate(CleanText(cols[1].InnerText), out var end)
                || !TryParsePrice(cols[4].InnerText, out var price))
            {
                continue;
            }

            decimal deposit = 0;
            if (cols.Count > 5)
                TryParsePrice(cols[5].InnerText, out deposit);

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

    private static bool IsLikelyLotImage(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var clean = href.Split('?', '#')[0];
        if (clean.Contains("/images/detailed/534/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (clean.Contains("logo", StringComparison.OrdinalIgnoreCase))
            return false;

        // CDN галереи РАД
        if (clean.Contains("/cdn/bkr/", StringComparison.OrdinalIgnoreCase))
            return true;

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

    private static string StripTags(string html) =>
        CleanText(Regex.Replace(html, "<[^>]+>", " "));

    public static bool TryParsePrice(string? rawText, out decimal price)
    {
        price = 0;
        if (string.IsNullOrWhiteSpace(rawText))
            return false;

        // На РАД часто "5 355 255.00" (точка как десятичный разделитель).
        var clean = CleanText(rawText)
            .Replace(" ", "")
            .Replace("\u00A0", "")
            .Replace("руб.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("руб", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₽", "")
            .Trim();

        if (clean.Contains('.') && clean.Contains(','))
        {
            // 1.234.567,89 → 1234567.89
            clean = clean.Replace(".", "").Replace(',', '.');
        }
        else if (clean.Contains(','))
        {
            clean = clean.Replace(',', '.');
        }

        return decimal.TryParse(
            clean,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out price);
    }

    public static bool TryParseMoscowDate(string? raw, out DateTime utc) =>
        AlfalotHtmlParser.TryParseMoscowDate(raw, out utc);
}
