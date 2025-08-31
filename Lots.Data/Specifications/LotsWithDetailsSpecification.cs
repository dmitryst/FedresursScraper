using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithDetailsSpecification : Specification<Lot>
{
    public LotsWithDetailsSpecification(
        int pageNumber,
        int pageSize,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null)
    {
        Query
            .Include(l => l.Bidding)
            .Include(l => l.Categories);

        if (!string.IsNullOrWhiteSpace(biddingType))
        {
            Query.Where(l => l.Bidding.Type == biddingType);
        }

        if (priceFrom.HasValue)
        {
            Query.Where(l => l.StartPrice >= priceFrom.Value);
        }

        if (priceTo.HasValue)
        {
            Query.Where(l => l.StartPrice <= priceTo.Value);
        }

        Query.OrderByDescending(l => l.Bidding.CreatedAt)
         .Skip((pageNumber - 1) * pageSize)
         .Take(pageSize);
    }
}
