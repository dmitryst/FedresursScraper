namespace Lots.Data.Entities
{
    public class LotCategory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Guid LotId { get; set; }
        public Lot Lot { get; set; }
    }
}