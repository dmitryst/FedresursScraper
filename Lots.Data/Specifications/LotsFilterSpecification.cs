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

            // Подготавливаем строку для ILike, чтобы искать все слова (в том же порядке, игнорируя знаки препинания между ними)
            var words = searchQuery.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var ilikeQuery = "%" + string.Join("%", words) + "%";

            Query.Where(l =>
                // Поиск по заголовку и описанию (FTS), используем Hunspell словарь
                l.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("russian_h", searchQuery)) ||

                // Поиск по вхождению слов в Title (помогает находить английские слова, которые игнорирует russian_h)
                EF.Functions.ILike(l.Title ?? "", ilikeQuery) ||

                // Поиск по кадастровым номерам (если в запросе есть цифры)
                (cleanSearchQuery.Length > 12 &&
                 l.CadastralNumbers.Any(cn => cn.CleanCadastralNumber.Contains(cleanSearchQuery)))
            );
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
}
