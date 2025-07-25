namespace Lots.Data.Entities
{
    public class Lot
    {
        public Guid Id { get; set; }
        public string BiddingType { get; set; }
        public string Url { get; set; }
        public string StartPrice { get; set; }
        public string Step { get; set; }
        public string Deposit { get; set; }
        public string Description { get; set; }
        public string ViewingProcedure { get; set; }
        public List<LotCategory> Categories { get; set; } = new();
    }
}