namespace Lots.Data.Entities
{
    public class Bidding
    {
        public Guid Id { get; set; }
        public DateTime? AnnouncedAt { get; set; }
        public string Type { get; set; } = default!;
        public string? BidAcceptancePeriod { get; set; }
        public Guid BankruptMessageId { get; set; }

        public string? ViewingProcedure { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<Lot> Lots { get; set; } = new();
    }
}