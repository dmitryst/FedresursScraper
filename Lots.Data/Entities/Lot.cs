using NpgsqlTypes;

namespace Lots.Data.Entities
{
    public class Lot
    {
        public Guid Id { get; set; }
        public string? LotNumber { get; set; }
        public decimal? StartPrice { get; set; }
        public decimal? Step { get; set; }
        public decimal? Deposit { get; set; }
        public string? Description { get; set; }
        public string? Title { get; set; }
        public bool IsSharedOwnership { get; set; }
        public string? ViewingProcedure { get; set; }
        public List<LotCategory> Categories { get; set; } = new();
        public List<LotCadastralNumber> CadastralNumbers { get; set; } = new();
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid BiddingId { get; set; }
        public Bidding Bidding { get; set; } = default!;

        // Техническое поле для хранения поискового индекса
        public NpgsqlTsVector SearchVector { get; set; } = default!;
    }
}