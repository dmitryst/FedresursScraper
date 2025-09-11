using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithCoordinatesSpecification : Specification<Lot>
{
    public LotsWithCoordinatesSpecification()
    {
        Query.Where(lot => lot.Latitude.HasValue && lot.Longitude.HasValue);
    }
}
