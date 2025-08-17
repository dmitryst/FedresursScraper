using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotByIdWithDetailsSpecification : Specification<Lot>, ISingleResultSpecification<Lot>
{
    public LotByIdWithDetailsSpecification(Guid lotId)
    {
        Query
            .Where(l => l.Id == lotId)
            .Include(l => l.Bidding)
            .Include(l => l.Categories);
    }
}
