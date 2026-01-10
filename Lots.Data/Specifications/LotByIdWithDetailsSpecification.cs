using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotByIdWithDetailsSpecification : Specification<Lot>, ISingleResultSpecification<Lot>
{
    /// <summary>
    /// Конструктор для поиска по lotId
    /// </summary>
    /// <param name="lotId"></param>
    public LotByIdWithDetailsSpecification(Guid lotId)
    {
        Query
            .Where(l => l.Id == lotId)
            .Include(l => l.Bidding)
            .Include(l => l.Categories)
            .Include(x => x.PriceSchedules)
            .Include(x => x.Images);
    }

    /// <summary>
    /// Конструктор для поиска по PublicId
    /// </summary>
    /// <param name="publicId"></param>
    public LotByIdWithDetailsSpecification(int publicId)
    {
        Query
            .Where(l => l.PublicId == publicId)
            .Include(l => l.Bidding)
            .Include(l => l.Categories)
            .Include(x => x.PriceSchedules)
            .Include(x => x.Images);
    }
}
