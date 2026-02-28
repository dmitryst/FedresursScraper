using Ardalis.Specification;
using Lots.Data.Entities;
using Lots.Data.Models;

namespace Lots.Data.Specifications;

public class LotsWithCoordinatesSpecification : Specification<Lot, LotGeoResult>
{
    public LotsWithCoordinatesSpecification(string[]? categories, bool onlyActive = true)
    {
        Query.Where(lot => lot.Latitude.HasValue && lot.Longitude.HasValue);

        if (categories != null && categories.Length > 0)
        {
            Query.Where(lot => lot.Categories.Any(c => categories.Contains(c.Name)));
        }

        // показываем только классифицированные лоты
        // лот считается классифицированным, если у него есть Title
        Query.Where(l => !string.IsNullOrEmpty(l.Title));

        // фильтрация по активности
        if (onlyActive)
        {
            // Используем доменный Expression
            Query.Where(Lot.IsActiveExpression);
        }

        Query.Select(lot => new LotGeoResult
        {
            Id = lot.Id,
            Title = lot.Title ?? lot.Description,
            StartPrice = lot.StartPrice,
            Latitude = lot.Latitude!.Value, 
            Longitude = lot.Longitude!.Value,
        });
        
        Query.AsNoTracking();
    }
}
