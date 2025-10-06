using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsCountSpecification : Specification<Lot>
{
    public LotsCountSpecification(
        string[]? categories,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null)
    {
        if (categories != null && categories.Length > 0)
        {
            Query.Where(l => l.Categories.Any(lc => categories.Contains(lc.Name)));
        }

        if (!string.IsNullOrWhiteSpace(biddingType) && biddingType != "Все")
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
    }
}