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
        /// Период торгов
        /// </summary>
        public string? TradePeriod { get; set; }

        /// <summary>
        /// Дата объявления результатов
        /// </summary>
        public DateTime? ResultsAnnouncementDate { get; set; }

        /// <summary>
        /// Организатор торгов
        /// </summary>
        public string? Organizer { get; set; }

        /// <summary>
        /// Должник
        /// </summary>
        public Guid? DebtorId { get; set; }
        public string? DebtorName { get; set; }
        public string? DebtorInn { get; set; }
        public string? DebtorSnils { get; set; }
        public string? DebtorOgrn { get; set; }
        public bool IsDebtorCompany { get; set; }

        /// <summary>
        /// Арбитражный управляющий
        /// </summary>
        public Guid? ArbitrationManagerId { get; set; }
        public string? ArbitrationManagerName { get; set; }
        public string? ArbitrationManagerInn { get; set; }

        /// <summary>
        /// Судебное дело
        /// </summary>
        public Guid? LegalCaseId { get; set; }
        public string? LegalCaseNumber { get; set; }

        /// <summary>
        /// Идентификатор сообщения о банкротстве с Федресурса
        /// </summary>
        public Guid? BankruptMessageId { get; set; }

        /// <summary>
        /// Порядок ознакомления с имуществом
        /// </summary>
        public string? ViewingProcedure { get; set; }
    }
}