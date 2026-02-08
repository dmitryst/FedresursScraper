using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithCoordinatesSpecification : Specification<Lot>
{
    public LotsWithCoordinatesSpecification(string[]? categories)
    {
        Query.Where(lot => lot.Latitude.HasValue && lot.Longitude.HasValue);

        Query.Include(l => l.Categories);

        if (categories != null && categories.Length > 0)
        {
            Query.Where(lot => lot.Categories.Any(c => categories.Contains(c.Name)));
        }

        // показываем только классифицированные лоты
        // Лот считается классифицированным, если у него есть Title
        Query.Where(l => !string.IsNullOrEmpty(l.Title));
        
        Query.AsNoTracking();
    }
}
