using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;

namespace Lots.Data.Specifications;

public class LotsWithDetailsSpecification : LotsFilterSpecification
{
    public LotsWithDetailsSpecification(
        int page,
        int pageSize,
        string[]? categories,
        string? searchQuery = null,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null,
        bool? isSharedOwnership = null)
        : base(categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership)
    {
        // Жадная загрузка
        Query
            .Include(l => l.Bidding)
            .Include(l => l.Categories);

        // Сортировка
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // Если есть поиск - сортируем по релевантности
            Query.OrderByDescending(l =>
                l.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("russian_h", searchQuery))
            );
        }
        else
        {
            // Иначе - по дате (свежие сверху)
            Query.OrderByDescending(l => l.Bidding.CreatedAt);
        }

        // Пагинация
        Query
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
