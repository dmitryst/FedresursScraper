using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FedresursScraper.Services;

public interface ITradeCardLotsStatusScraper
{
    /// <summary>
    /// Парсит статусы/результаты по указанным лотам со страницы
    /// https://old.bankrot.fedresurs.ru/TradeCard.aspx?ID={biddingId}
    /// </summary>
    Task<IReadOnlyDictionary<string, TradeCardLotStatus>> ScrapeLotsStatusesAsync(
        Guid biddingId,
        IEnumerable<string> lotNumbers,
        CancellationToken token);
}

public sealed record TradeCardLotStatus(
    string LotNumber,
    string TradeStatus,
    decimal? FinalPrice,
    string? WinnerName,
    string? WinnerInn);

public sealed class TradeCardLotsStatusScraper : ITradeCardLotsStatusScraper
{
    private const string BaseUrl = "https://old.bankrot.fedresurs.ru";

    private static readonly Regex InnRegex = new(@"\b\d{10}(\d{2})?\b", RegexOptions.Compiled);
    private static readonly Regex LotHeaderRegex = new(
        @"^\s*Лот\s*№\s*(?<n>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ILogger<TradeCardLotsStatusScraper> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HtmlParser _htmlParser = new();

    public TradeCardLotsStatusScraper(
        ILogger<TradeCardLotsStatusScraper> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyDictionary<string, TradeCardLotStatus>> ScrapeLotsStatusesAsync(
        Guid biddingId,
        IEnumerable<string> lotNumbers,
        CancellationToken token)
    {
        var result = new Dictionary<string, TradeCardLotStatus>(StringComparer.OrdinalIgnoreCase);

        var lotNumbersList = lotNumbers
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => NormalizeLotNumber(n!))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (lotNumbersList.Count == 0)
        {
            return result;
        }

        var doc = await LoadTradeCardDocumentAsync(biddingId, token);
        if (doc is null)
        {
            return result;
        }

        foreach (var lotNumber in lotNumbersList)
        {
            try
            {
                var parsed = TryParseLot(doc, lotNumber);
                if (parsed is null) continue;

                result[lotNumber] = parsed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при парсинге TradeCard для biddingId={BiddingId}, lot={LotNumber}", biddingId, lotNumber);
            }
        }

        return result;
    }

    private async Task<IDocument?> LoadTradeCardDocumentAsync(Guid biddingId, CancellationToken token)
    {
        var url = $"{BaseUrl}/TradeCard.aspx?ID={biddingId}";

        try
        {
            var client = _httpClientFactory.CreateClient("FedresursScraper");
            using var response = await client.GetAsync(url, token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Не удалось загрузить TradeCard. biddingId={BiddingId}, status={StatusCode}", biddingId, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(token);
            return await _htmlParser.ParseDocumentAsync(html, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при загрузке TradeCard. biddingId={BiddingId}", biddingId);
            return null;
        }
    }

    private TradeCardLotStatus? TryParseLot(IDocument doc, string normalizedLotNumber)
    {
        // Надежная стратегия для страниц с несколькими лотами:
        // 1) находим таблицу, где есть строка "Статус торгов"
        // 2) определяем, к какому "Лот № X" относится эта таблица (по ближайшему заголовку лота выше по DOM)
        // 3) парсим значения только из этой таблицы (без смешивания с другими лотами)
        var rows = ExtractStatusTableRowsForLot(doc, normalizedLotNumber);
        if (rows is null || rows.Count == 0)
        {
            _logger.LogDebug("TradeCard: не найдена таблица статусов для лота LotNumber={LotNumber}", normalizedLotNumber);
            return null;
        }

        var rawStatus = FindValue(rows, key => ContainsIgnoreCase(key, "Статус торгов"));
        if (IsEmptyValue(rawStatus))
        {
            // На некоторых страницах нет статуса (или другой шаблон) — тогда не обновляем.
            _logger.LogDebug("TradeCard: не найден 'Статус торгов' для лота LotNumber={LotNumber}", normalizedLotNumber);
            return null;
        }

        var rawFinalPrice = FindValue(rows, key => ContainsIgnoreCase(key, "Итоговая") && ContainsIgnoreCase(key, "цена"));
        var finalPrice = TryParseMoney(rawFinalPrice);

        var winnerIndex = FindIndex(rows, key => ContainsIgnoreCase(key, "Победитель"));
        string? winnerName = winnerIndex >= 0 ? NormalizeValue(rows[winnerIndex].Value) : null;

        string? winnerInn = null;
        if (!IsEmptyValue(winnerName))
        {
            winnerInn = ExtractInnFromText(winnerName);

            if (winnerInn is null)
            {
                // Часто ИНН идёт следующим полем после "Победитель"
                for (int i = winnerIndex + 1; i < rows.Count; i++)
                {
                    if (ContainsIgnoreCase(rows[i].Key, "ИНН"))
                    {
                        winnerInn = ExtractInnFromText(rows[i].Value) ?? NormalizeValue(rows[i].Value);
                        break;
                    }
                }
            }
        }

        var baseStatus = NormalizeValue(rawStatus) ?? rawStatus!.Trim();
        var computedStatus = baseStatus;

        // Правило: если торги "Завершенные", но нет итоговой цены — значит торги признаны несостоявшимися (нет победителя)
        if (string.Equals(baseStatus, "Завершенные", StringComparison.OrdinalIgnoreCase))
        {
            if (!finalPrice.HasValue)
            {
                computedStatus = "Торги не состоялись";
                winnerName = null;
                winnerInn = null;
            }
        }

        // Если итоговая цена отсутствует — очищаем финальные поля (на случай кривых страниц)
        if (!finalPrice.HasValue)
        {
            winnerName = null;
            winnerInn = null;
        }

        // Если статус не "Завершенные" — финальные поля нам не нужны
        if (!string.Equals(computedStatus, "Завершенные", StringComparison.OrdinalIgnoreCase))
        {
            finalPrice = null;
            winnerName = null;
            winnerInn = null;
        }

        return new TradeCardLotStatus(
            LotNumber: normalizedLotNumber,
            TradeStatus: computedStatus,
            FinalPrice: finalPrice,
            WinnerName: winnerName,
            WinnerInn: winnerInn);
    }

    private static List<(string Key, string Value)>? ExtractStatusTableRowsForLot(IDocument doc, string normalizedLotNumber)
    {
        // Находим все строки таблиц, где слева "Статус торгов"
        var statusRows = doc.QuerySelectorAll("tr")
            .OfType<IElement>()
            .Where(tr =>
            {
                var tds = tr.QuerySelectorAll("td").ToArray();
                if (tds.Length < 2) return false;

                var key = NormalizeKey(tds[0].TextContent);
                return !string.IsNullOrWhiteSpace(key) && ContainsIgnoreCase(key, "Статус торгов");
            })
            .ToList();

        if (statusRows.Count == 0) return null;

        var all = doc.All.ToList();

        foreach (var tr in statusRows)
        {
            var lotNumber = FindNearestLotNumberAbove(all, tr);
            if (lotNumber is null) continue;

            lotNumber = NormalizeLotNumber(lotNumber);
            if (!string.Equals(lotNumber, normalizedLotNumber, StringComparison.OrdinalIgnoreCase)) continue;

            var table = tr.Closest("table");
            if (table is null) continue;

            return ExtractKeyValueRows(table);
        }

        return null;
    }

    private static string? FindNearestLotNumberAbove(List<IElement> allElements, IElement startElement)
    {
        var idx = allElements.IndexOf(startElement);
        if (idx < 0) return null;

        for (int i = idx; i >= 0; i--)
        {
            var text = NormalizeSpaces(allElements[i].TextContent).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var m = LotHeaderRegex.Match(text);
            if (m.Success)
            {
                return m.Groups["n"].Value;
            }
        }

        return null;
    }

    private static IElement? FindBestLotContainer(IDocument doc, Regex lotRegex)
    {
        // Ищем элемент, который одновременно содержит "Лот № X" и таблицу/строку со статусом торгов.
        // Берём самый "маленький" по TextContent, чтобы сузить контейнер как можно ближе к лоту.
        var candidates = doc.All
            .Where(e =>
            {
                var t = e.TextContent;
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (!t.Contains("Лот", StringComparison.OrdinalIgnoreCase)) return false;
                if (!lotRegex.IsMatch(t)) return false;
                if (!t.Contains("Статус торгов", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            })
            .OrderBy(e => e.TextContent.Length)
            .ToList();

        if (candidates.Count > 0) return candidates[0];

        // Фолбек: если текст "Статус торгов" не попал внутрь того же контейнера, ищем просто по "Лот №"
        return doc.All
            .Where(e =>
            {
                var t = e.TextContent;
                if (string.IsNullOrWhiteSpace(t)) return false;
                if (!t.Contains("Лот", StringComparison.OrdinalIgnoreCase)) return false;
                return lotRegex.IsMatch(t);
            })
            .OrderBy(e => e.TextContent.Length)
            .FirstOrDefault();
    }

    private static List<(string Key, string Value)> ExtractKeyValueRows(IElement container)
    {
        var rows = new List<(string Key, string Value)>();

        foreach (var tr in container.QuerySelectorAll("tr"))
        {
            var tds = tr.QuerySelectorAll("td").ToArray();
            if (tds.Length < 2) continue;

            var key = NormalizeKey(tds[0].TextContent);
            var value = NormalizeValue(tds[1].TextContent);

            if (string.IsNullOrWhiteSpace(key)) continue;
            rows.Add((key!, value ?? string.Empty));
        }

        return rows;
    }

    private static string NormalizeLotNumber(string lotNumber)
    {
        var s = lotNumber.Trim();
        s = Regex.Replace(s, @"(?i)\s*лот\s*№?\s*", "");
        return s.Trim();
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return NormalizeSpaces(key).Trim().Trim(':');
    }

    private static string? NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = NormalizeSpaces(value).Trim();
        if (IsEmptyValue(v)) return null;
        return v;
    }

    private static bool IsEmptyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.Equals("не найдено", StringComparison.OrdinalIgnoreCase)
               || v.Equals("не установлено", StringComparison.OrdinalIgnoreCase)
               || v.Equals("не указано", StringComparison.OrdinalIgnoreCase)
               || v.Equals("-", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSpaces(string s)
    {
        // Убираем неразрывные пробелы и схлопываем множественные пробелы
        return Regex.Replace(s.Replace('\u00A0', ' '), @"\s+", " ");
    }

    private static bool ContainsIgnoreCase(string? s, string fragment) =>
        !string.IsNullOrEmpty(s) && s.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static string? FindValue(List<(string Key, string Value)> rows, Func<string, bool> keyPredicate)
    {
        foreach (var (k, v) in rows)
        {
            if (keyPredicate(k)) return v;
        }
        return null;
    }

    private static int FindIndex(List<(string Key, string Value)> rows, Func<string, bool> keyPredicate)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (keyPredicate(rows[i].Key)) return i;
        }
        return -1;
    }

    private static string? ExtractInnFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = InnRegex.Match(text);
        return m.Success ? m.Value : null;
    }

    private static decimal? TryParseMoney(string? raw)
    {
        if (IsEmptyValue(raw)) return null;

        var clean = raw!
            .Replace("руб.", "", StringComparison.OrdinalIgnoreCase)
            .Replace("руб", "", StringComparison.OrdinalIgnoreCase)
            .Replace("₽", "")
            .Replace("\u00A0", "")
            .Replace(" ", "")
            .Trim();

        // "489960,00" -> "489960.00"
        clean = clean.Replace(",", ".");

        // Если точек больше одной (разделители тысяч 1.000.000.00), удаляем все кроме последней
        if (clean.Count(c => c == '.') > 1)
        {
            var lastDot = clean.LastIndexOf('.');
            clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot);
        }

        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }
}

