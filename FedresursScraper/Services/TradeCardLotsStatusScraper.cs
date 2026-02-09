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

    private static readonly Regex DoPostBackRegex = new(
        @"__doPostBack\('(?<t>[^']+)'\s*,\s*'(?<a>[^']*)'\)",
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

        var remaining = new HashSet<string>(lotNumbersList, StringComparer.OrdinalIgnoreCase);

        var client = _httpClientFactory.CreateClient("FedresursScraper");
        var url = $"{BaseUrl}/TradeCard.aspx?ID={biddingId}";

        var firstDoc = await LoadDocumentAsync(client, url, token);
        if (firstDoc is null) return result;

        // Парсим первую страницу и далее обходим все страницы по пагинации (если есть).
        var visitedPageNumbers = new HashSet<int> { 1 };
        var discoveredNumericPages = new SortedSet<int>();
        string? pagerTarget = null;

        var currentDoc = firstDoc;
        var safetyIterations = 0;

        while (currentDoc != null && safetyIterations++ < 200)
        {
            // 1) Парсим результаты для нужных лотов с текущей страницы
            foreach (var lotNumber in remaining.ToList())
            {
                try
                {
                    var parsed = TryParseLot(currentDoc, lotNumber);
                    if (parsed is null) continue;

                    result[lotNumber] = parsed;
                    remaining.Remove(lotNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при парсинге TradeCard для biddingId={BiddingId}, lot={LotNumber}", biddingId, lotNumber);
                }
            }

            if (remaining.Count == 0)
            {
                break;
            }

            // 2) Находим пагинацию (если есть) и обходим оставшиеся страницы
            var postbacks = ParsePostBackLinks(currentDoc);
            if (postbacks.Count == 0)
            {
                break;
            }

            pagerTarget ??= ChoosePagerTarget(postbacks);
            if (pagerTarget is null)
            {
                break;
            }

            bool IsMatch(string t) =>
                string.Equals(t, pagerTarget, StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(pagerTarget + "$", StringComparison.OrdinalIgnoreCase);

            // Собираем все видимые номера страниц
            foreach (var pb in postbacks.Where(p => IsMatch(p.Target) && p.PageNumber is int))
            {
                discoveredNumericPages.Add(pb.PageNumber!.Value);
            }

            // Пытаемся перейти на следующую не посещенную страницу (сначала по номеру).
            int nextPage = discoveredNumericPages.FirstOrDefault(p => p != 1 && !visitedPageNumbers.Contains(p));

            TradeCardPostBackLink? nextLink = null;
            if (nextPage != 0)
            {
                nextLink = postbacks.FirstOrDefault(p => IsMatch(p.Target) && p.PageNumber == nextPage);
            }

            // Фолбек: если номера страниц не “видны” (скользящее окно пагинации) — кликаем Page$Next, пока можем.
            if (nextLink is null)
            {
                // Ищем ссылки "..." или ">>" или "Page$Next"
                // ВАЖНО: исключаем те, у которых PageNumber указывает на уже посещенную страницу.
                // Это предотвращает клик по первой ссылке "..." (Назад), которая ведет на старые страницы.
                nextLink = postbacks.LastOrDefault(p =>
                    IsMatch(p.Target) &&
                    (string.Equals(p.Argument, "Page$Next", StringComparison.OrdinalIgnoreCase) ||
                     p.Text == "..." || p.Text == "…" || p.Text == ">>") &&
                    (!p.PageNumber.HasValue || !visitedPageNumbers.Contains(p.PageNumber.Value)));
            }

            if (nextLink is null)
            {
                break;
            }

            // 3) Делаем POST-back на следующую страницу
            var form = ExtractAspNetFormFields(currentDoc);
            form["__EVENTTARGET"] = nextLink.Target;
            form["__EVENTARGUMENT"] = nextLink.Argument;
            // Лучше передать пустой фокус, чтобы сервер не пытался скроллить к элементу
            if (form.ContainsKey("__LASTFOCUS")) form["__LASTFOCUS"] = "";

            currentDoc = await PostBackAsync(client, url, form, token);

            // Попытка вычислить номер страницы после перехода — если смогли, используем его для защиты от циклов.
            var currentPage = TryDetectCurrentPageNumber(currentDoc, pagerTarget);
            if (currentPage.HasValue)
            {
                if (!visitedPageNumbers.Add(currentPage.Value))
                {
                    // Уже были на этой странице — значит, попали в цикл.
                    break;
                }
            }
            else
            {
                // Если не смогли определить номер страницы, хотя бы отметим "мы пытались перейти по nextPage".
                if (nextLink.PageNumber.HasValue)
                {
                    visitedPageNumbers.Add(nextLink.PageNumber.Value);
                }
            }
        }

        return result;
    }

    private async Task<IDocument?> LoadDocumentAsync(HttpClient client, string url, CancellationToken token)
    {
        try
        {
            using var response = await client.GetAsync(url, token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Не удалось загрузить страницу. url={Url}, status={StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(token);
            return await _htmlParser.ParseDocumentAsync(html, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при загрузке страницы. url={Url}", url);
            return null;
        }
    }

    private async Task<IDocument?> PostBackAsync(HttpClient client, string url, Dictionary<string, string> form, CancellationToken token)
    {
        try
        {
            using var response = await client.PostAsync(url, new FormUrlEncodedContent(form), token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Не удалось выполнить postback. url={Url}, status={StatusCode}", url, response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(token);
            return await _htmlParser.ParseDocumentAsync(html, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при postback. url={Url}", url);
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

    private sealed record TradeCardPostBackLink(string Target, string Argument, int? PageNumber, string Text);

    private static List<TradeCardPostBackLink> ParsePostBackLinks(IDocument doc)
    {
        var links = new List<TradeCardPostBackLink>();

        foreach (var a in doc.QuerySelectorAll("a").OfType<IElement>())
        {
            var href = a.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (!href.Contains("__doPostBack", StringComparison.OrdinalIgnoreCase)) continue;

            var m = DoPostBackRegex.Match(href);
            if (!m.Success) continue;

            var target = m.Groups["t"].Value;
            var arg = m.Groups["a"].Value;
            var text = NormalizeSpaces(a.TextContent).Trim();

            var pageNumber = TryParsePageNumber(arg);
            if (pageNumber is null && !string.IsNullOrWhiteSpace(text) && int.TryParse(text, out var p))
            {
                pageNumber = p;
            }

            links.Add(new TradeCardPostBackLink(
                Target: target,
                Argument: arg,
                PageNumber: pageNumber,
                Text: text));
        }

        return links;
    }

    private static int? TryParsePageNumber(string? eventArgument)
    {
        if (string.IsNullOrWhiteSpace(eventArgument)) return null;
        var arg = eventArgument.Trim();

        if (!arg.StartsWith("Page$", StringComparison.OrdinalIgnoreCase)) return null;

        var tail = arg.Substring("Page$".Length);
        if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
        {
            return page;
        }

        return null;
    }

    private static string? ChoosePagerTarget(List<TradeCardPostBackLink> postbacks)
    {
        var withPages = postbacks.Where(p => p.PageNumber.HasValue).ToList();
        if (withPages.Count == 0) return null;

        // 1. Try exact match (Argument based)
        var bestExact = withPages
            .GroupBy(p => p.Target, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Target = g.Key, Pages = g.Select(x => x.PageNumber!.Value).Distinct().Count() })
            .OrderByDescending(x => x.Pages)
            .FirstOrDefault();

        if (bestExact != null && bestExact.Pages > 1) return bestExact.Target;

        // 2. Try prefix match (Text based, distinct targets)
        var bestPrefix = withPages
            .GroupBy(p =>
            {
                var idx = p.Target.LastIndexOf('$');
                return idx > 0 ? p.Target.Substring(0, idx) : p.Target;
            }, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Target = g.Key, Pages = g.Select(x => x.PageNumber!.Value).Distinct().Count() })
            .OrderByDescending(x => x.Pages)
            .FirstOrDefault();

        if (bestPrefix != null && bestPrefix.Pages > 1) return bestPrefix.Target;

        return bestExact?.Target ?? bestPrefix?.Target;
    }

    private static int? TryDetectCurrentPageNumber(IDocument? doc, string pagerTarget)
    {
        if (doc is null) return null;

        // Частый случай ASP.NET GridView: текущая страница рендерится как <span>1</span> внутри ".pager".
        // Мы берем первую чисто числовую span из любого контейнера с class "pager".
        var pagerContainers = doc.QuerySelectorAll(".pager").OfType<IElement>().ToList();
        foreach (var pager in pagerContainers)
        {
            foreach (var span in pager.QuerySelectorAll("span").OfType<IElement>())
            {
                var t = NormalizeSpaces(span.TextContent).Trim();
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
                {
                    return page;
                }
            }
        }

        // Фолбек: если ".pager" не найден — пробуем найти span с числом рядом с ссылками __doPostBack(pagerTarget,...)
        // (очень грубый, но лучше чем ничего).
        var all = doc.All.ToList();
        for (int i = 0; i < all.Count; i++)
        {
            if (!string.Equals(all[i].TagName, "A", StringComparison.OrdinalIgnoreCase)) continue;
            var href = all[i].GetAttribute("href") ?? "";
            var m = DoPostBackRegex.Match(href);
            if (!m.Success) continue;

            var target = m.Groups["t"].Value;
            if (!string.Equals(target, pagerTarget, StringComparison.OrdinalIgnoreCase) &&
                !target.StartsWith(pagerTarget + "$", StringComparison.OrdinalIgnoreCase)) continue;

            // ищем span в окрестности
            for (int j = Math.Max(0, i - 20); j < Math.Min(all.Count, i + 20); j++)
            {
                if (!string.Equals(all[j].TagName, "SPAN", StringComparison.OrdinalIgnoreCase)) continue;
                var t = NormalizeSpaces(all[j].TextContent).Trim();
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
                {
                    return page;
                }
            }

            break;
        }

        return null;
    }

    private static Dictionary<string, string> ExtractAspNetFormFields(IDocument doc)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Собираем ВСЕ input поля.
        // Telerik и ASP.NET WebForms используют множество hidden полей для хранения состояния (ClientState и др.).
        // Если их не передать, сервер подумает, что сессия/состояние сбросилось, и вернет первую страницу.
        foreach (var input in doc.QuerySelectorAll("input"))
        {
            var name = input.GetAttribute("name");
            var val = input.GetAttribute("value");
            var type = input.GetAttribute("type");

            if (string.IsNullOrWhiteSpace(name)) continue;

            // Не включаем кнопки submit/button/image, так как их включение эмулирует клик по ним.
            // Клик мы эмулируем через __EVENTTARGET.
            if (string.Equals(type, "submit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "button", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Пропускаем __VIEWSTATEENCRYPTED, если он пустой (иначе может вызвать ошибку валидации).
            if (string.Equals(name, "__VIEWSTATEENCRYPTED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            if (val != null)
            {
                fields[name] = val;
            }
        }

        // Гарантируем наличие обязательных системных полей
        if (!fields.ContainsKey("__EVENTTARGET")) fields["__EVENTTARGET"] = "";
        if (!fields.ContainsKey("__EVENTARGUMENT")) fields["__EVENTARGUMENT"] = "";

        return fields;
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

