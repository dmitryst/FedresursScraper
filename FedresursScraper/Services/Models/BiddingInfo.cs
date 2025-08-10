namespace FedresursScraper.Services.Models
{
    /// <summary>
    /// Торги
    /// </summary>
    public class BiddingInfo
    {
        /// <summary>
        /// Идентификатор торгов с Федресурса
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Дата объявления торгов
        /// </summary>
        public DateTime? AnnouncedAt { get; set; }

        /// <summary>
        /// Тип торгов
        /// </summary>
        public string Type { get; set; } = default!;

        /// <summary>
        /// Период приема заявок
        /// </summary>
        public string? BidAcceptancePeriod { get; set; }

        /// <summary>
        /// Идентификатор сообщения о банкростве с Федресурса
        /// </summary>
        public Guid? BankruptMessageId { get; set; }

        /// <summary>
        /// Порядок ознакомления с имуществом
        /// </summary>
        public string? ViewingProcedure { get; set; }

        /// <summary>
        /// Дата создания записи в БД
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Лоты
        /// </summary>
        public List<LotInfo> Lots { get; set; } = new();
    }
}