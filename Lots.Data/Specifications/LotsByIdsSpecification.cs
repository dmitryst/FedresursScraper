// Data/Specifications/LotsByIdsSpecification.cs
using Ardalis.Specification;
using Lots.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lots.Data.Specifications
{
    public class LotsByIdsSpecification : Specification<Lot>
    {
    public LotsByIdsSpecification(IEnumerable<Guid> lotIds)
    {
        Query.Where(l => lotIds.Contains(l.Id))
             .Include(l => l.Bidding)
             .Include(l => l.Categories)
             .Include(l => l.Images)
             .Include(l => l.Documents)
             .Include(l => l.PriceSchedules);
    }
    }
}