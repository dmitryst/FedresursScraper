using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithDetailsSpecification : Specification<Lot>
{
    public LotsWithDetailsSpecification(int pageNumber, int pageSize, string? biddingType = null)
    {
        Query
            .Include(l => l.Bidding)
            .Include(l => l.Categories);

        if (!string.IsNullOrWhiteSpace(biddingType))
        {
            Query.Where(l => l.Bidding.Type == biddingType);
        }

        Query.OrderByDescending(l => l.Bidding.CreatedAt)
         .Skip((pageNumber - 1) * pageSize)
         .Take(pageSize);
    }
}
