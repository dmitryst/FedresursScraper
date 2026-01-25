using NpgsqlTypes;

namespace Lots.Data.Entities
{
    public class Lot
    {
        /// <summary>
        /// Внутренний уникальный идентификатор лота
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Глобальный сквозной номер лота для URL и поиска
        /// </summary>
        public int PublicId { get; set; }

        /// <summary>
        /// Номер лота внутри торгов (указан в торгах Федресурса)
        /// </summary>
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
        public ICollection<LotImage> Images { get; set; } = new List<LotImage>();
        public ICollection<LotDocument> Documents { get; set; } = new List<LotDocument>();
        public ICollection<LotPriceSchedule> PriceSchedules { get; set; } = new List<LotPriceSchedule>();
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid BiddingId { get; set; }
        public Bidding Bidding { get; set; } = default!;

        /// <summary>
        /// Код региона местонахождения имущества (первые две цифры ИНН должника)
        /// </summary>
        public string? PropertyRegionCode { get; set; }

        /// <summary>
        /// Название региона местонахождения имущества
        /// </summary>
        public string? PropertyRegionName { get; set; }

        /// <summary>
        /// Полный адрес местонахождения имущества (если указан в описании)
        /// </summary>
        public string? PropertyFullAddress { get; set; }

        // Техническое поле для хранения поискового индекса
        public NpgsqlTsVector SearchVector { get; set; } = default!;
    }
}