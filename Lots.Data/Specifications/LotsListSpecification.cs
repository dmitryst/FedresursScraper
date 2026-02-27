using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;

namespace Lots.Data.Specifications;

/// <summary>
/// Спецификация для загрузки списка лотов с базовыми данными для отображения в карточках
/// </summary>
public class LotsListSpecification : LotsFilterSpecification
{
    public LotsListSpecification(
        int page,
        int pageSize,
        string[]? categories,
        string? searchQuery = null,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null,
        bool? isSharedOwnership = null,
        string[]? regions = null,
        bool onlyActive = true)
        : base(categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership, regions, onlyActive)
    {
        Query.AsNoTracking();
        
        // Жадная загрузка базовых данных для списка лотов
        Query
            .Include(l => l.Bidding)
            .Include(l => l.Categories)
            .Include(l => l.Images);

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
