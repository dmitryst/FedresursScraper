using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsCountSpecification : Specification<Lot>
{
    public LotsCountSpecification(string? biddingType = null)
    {
        if (!string.IsNullOrWhiteSpace(biddingType))
        {
            Query.Where(l => l.Bidding.Type == biddingType);
        }
    }
}