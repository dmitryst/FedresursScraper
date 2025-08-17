using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithDetailsSpecification : Specification<Lot>
{
    public LotsWithDetailsSpecification(int pageNumber, int pageSize)
    {
        Query
            .Include(l => l.Bidding)
            .Include(l => l.Categories)
            .OrderByDescending(l => l.Bidding.AnnouncedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);
    }
}
