using Ardalis.Specification;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lots.Data.Specifications;

public class LotsFilterSpecification : Specification<Lot>
{
    public LotsFilterSpecification(
        string[]? categories,
        string? searchQuery = null,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null)
    {
        // Полнотекстовый поиск + Кадастровые номера
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // Очищаем запрос от мусора для поиска по кадастру (оставляем только цифры)
            var cleanSearchQuery = new string(searchQuery.Where(char.IsDigit).ToArray());

            Query.Where(l =>
                // Поиск по заголовку и описанию (FTS)
                l.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("russian", searchQuery)) ||

                // Поиск по точному вхождению фразы в Title (для коротких названий)
                EF.Functions.ILike(l.Title ?? "", $"%{searchQuery}%") ||

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
            Query.Where(l => l.Bidding.Type == biddingType);
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
    }
}
