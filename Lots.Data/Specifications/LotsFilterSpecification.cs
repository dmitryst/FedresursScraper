using Ardalis.Specification;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lots.Data.Specifications;

public class LotsFilterSpecification : Specification<Lot>
{
    private static readonly HashSet<string> CaseInsensitiveAttributeFilterKeys =
        new(StringComparer.OrdinalIgnoreCase) { "brand", "model" };

    public LotsFilterSpecification(
        string[]? categories,
        string? searchQuery = null,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null,
        bool? isSharedOwnership = null,
        string[]? regions = null,
        bool onlyActive = true,
        Dictionary<string, string>? dynamicFilters = null)
    {
        // Полнотекстовый поиск + Кадастровые номера
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // Очищаем запрос от мусора для поиска по кадастру (оставляем только цифры)
            var cleanSearchQuery = new string(searchQuery.Where(char.IsDigit).ToArray());

            // Полный кадастровый номер: только цифры и разделители, достаточно цифр для B-tree lookup.
            // Equality попадает в IX_LotCadastralNumbers_CleanCadastralNumber; Contains/ILIKE '%…%' — нет.
            if (IsCadastralSearchQuery(searchQuery, cleanSearchQuery))
            {
                Query.Where(l =>
                    l.CadastralNumbers.Any(cn => cn.CleanCadastralNumber == cleanSearchQuery));
            }
            else
            {
                // FTS по SearchVector уже покрыт GIN (IX_Lots_SearchVector).
                // ILIKE '%…%' без trigram-индекса часто заставляет Postgres seq-scan'ить всю таблицу
                // даже при OR с быстрым FTS — поэтому для кириллицы оставляем только FTS.
                // ILIKE нужен для латиницы (BMW, Toyota…), которую russian_h может не токенизировать.
                if (ContainsLatinLetter(searchQuery))
                {
                    var words = searchQuery.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var ilikeQuery = "%" + string.Join("%", words) + "%";

                    Query.Where(l =>
                        l.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("russian_h", searchQuery)) ||
                        EF.Functions.ILike(l.Title ?? "", ilikeQuery));
                }
                else
                {
                    Query.Where(l =>
                        l.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("russian_h", searchQuery)));
                }
            }
        }

        // Фильтр по категориям
        if (categories != null && categories.Length > 0)
        {
            Query.Where(l => l.Categories.Any(lc => categories.Contains(lc.Name)));
        }

        // Фильтр по типу торгов
        if (!string.IsNullOrWhiteSpace(biddingType) && biddingType != "Все")
        {
            if (biddingType == "Открытый аукцион")
            {
                var auctionTypes = new[] { 
                    "Открытый аукцион", 
                    "Закрытый аукцион", 
                    "Открытый конкурс", 
                    "Закрытый конкурс", 
                    "Открытая форма подачи предложений о цене" 
                };
                Query.Where(l => auctionTypes.Contains(l.Bidding.Type));
            }
            else if (biddingType == "Публичное предложение")
            {
                var publicOfferTypes = new[] { 
                    "Публичное предложение", 
                    "Закрытое публичное предложение" 
                };
                Query.Where(l => publicOfferTypes.Contains(l.Bidding.Type));
            }
            else
            {
                Query.Where(l => l.Bidding.Type == biddingType);
            }
        }

        // Фильтр по начальной цене
        if (priceFrom.HasValue)
        {
            Query.Where(l => l.StartPrice >= priceFrom.Value);
        }

        if (priceTo.HasValue)
        {
            Query.Where(l => l.StartPrice <= priceTo.Value);
        }

        // Фильтр по долевой собственности
        // Если параметр не передан (null) - показываем всё (и доли, и не доли)
        // Если передан true - только доли
        // Если передан false - только НЕ доли
        if (isSharedOwnership.HasValue)
        {
            Query.Where(l => l.IsSharedOwnership == isSharedOwnership.Value);
        }

        // Фильтр по регионам (местонахождение имущества)
        if (regions != null && regions.Length > 0)
        {
            Query.Where(l => !string.IsNullOrEmpty(l.PropertyRegionName) && regions.Contains(l.PropertyRegionName));
        }

        // показываем только классифицированные лоты
        // Лот считается классифицированным, если у него есть Title
        Query.Where(l => !string.IsNullOrEmpty(l.Title));

        // Фильтрация по активности
        if (onlyActive)
        {
            Query.Where(Lot.IsActiveExpression);
        }

        // Динамические фильтры (атрибуты)
        if (dynamicFilters != null && dynamicFilters.Any())
        {
            foreach (var filter in dynamicFilters)
            {
                var key = filter.Key;
                var value = filter.Value;

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                // Обработка диапазонов (от/до)
                if (key.EndsWith("_from"))
                {
                    var actualKey = key.Substring(0, key.Length - 5);
                    if (decimal.TryParse(value, out var numValue))
                    {
                        // Строгая фильтрация: атрибут должен существовать и удовлетворять условию
                        Query.Where(l => l.Attributes != null && 
                            EF.Functions.JsonExists(l.Attributes, actualKey) && 
                            Convert.ToDecimal(LotsDbContext.JsonbExtractPathText(l.Attributes, actualKey)) >= numValue);
                    }
                }
                else if (key.EndsWith("_to"))
                {
                    var actualKey = key.Substring(0, key.Length - 3);
                    if (decimal.TryParse(value, out var numValue))
                    {
                        // Строгая фильтрация: атрибут должен существовать и удовлетворять условию
                        Query.Where(l => l.Attributes != null && 
                            EF.Functions.JsonExists(l.Attributes, actualKey) && 
                            Convert.ToDecimal(LotsDbContext.JsonbExtractPathText(l.Attributes, actualKey)) <= numValue);
                    }
                }
                else if (CaseInsensitiveAttributeFilterKeys.Contains(key))
                {
                    var matchValue = value.Trim();
                    Query.Where(l => l.Attributes != null &&
                        EF.Functions.JsonExists(l.Attributes, key) &&
                        LotsDbContext.JsonbExtractPathText(l.Attributes, key).ToLower() == matchValue.ToLower());
                }
                else
                {
                    // Точное совпадение (используем JsonContains для надежной трансляции в Postgres)
                    // Строгая фильтрация: атрибут должен существовать и совпадать
                    Query.Where(l => l.Attributes != null && 
                        EF.Functions.JsonContains(l.Attributes, $"{{\"{key}\": \"{value}\"}}"));
                }
            }
        }
    }

    /// <summary>
    /// Запрос вида кадастрового номера (например 50:17:0000000:38629) —
    /// ищем точным совпадением CleanCadastralNumber, чтобы сработал B-tree индекс.
    /// </summary>
    internal static bool IsCadastralSearchQuery(string searchQuery, string? cleanSearchQuery = null)
    {
        cleanSearchQuery ??= new string(searchQuery.Where(char.IsDigit).ToArray());
        if (cleanSearchQuery.Length <= 12)
            return false;

        return searchQuery.All(c => char.IsDigit(c) || c is ':' or '-' or ' ' or '\t');
    }

    private static bool ContainsLatinLetter(string value) =>
        value.Any(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
}
