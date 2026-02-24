using Ardalis.Specification;
using Lots.Data.Entities;

namespace Lots.Data.Specifications;

public class LotsWithCoordinatesSpecification : Specification<Lot>
{
    public LotsWithCoordinatesSpecification(string[]? categories, bool onlyActive = true)
    {
        Query.Where(lot => lot.Latitude.HasValue && lot.Longitude.HasValue);

        Query.Include(l => l.Categories);

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
            // берем только те лоты, у которых статус либо пустой, либо не входит в список финальных
            Query.Where(l => l.TradeStatus == null || l.TradeStatus == "" || 
                             !Lot.FinalTradeStatuses.Contains(l.TradeStatus));
        }
        
        Query.AsNoTracking();
    }
}
