using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsCountSpecification : Specification<Lot>
{
    public LotsCountSpecification(
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null)
    {
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
    }
}