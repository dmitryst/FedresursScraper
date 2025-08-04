namespace Lots.Data.Entities
{
    public class Lot
    {
        public Guid Id { get; set; }
        public string BiddingType { get; set; }
        public string Url { get; set; }
        public decimal? StartPrice { get; set; }
        public decimal? Step { get; set; }
        public decimal? Deposit { get; set; }
        public string Description { get; set; }
        public string ViewingProcedure { get; set; }
        public List<LotCategory> Categories { get; set; } = new();
        public DateTime? BiddingAnnouncementDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}