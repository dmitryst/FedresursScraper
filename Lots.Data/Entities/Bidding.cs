using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities
{
    /// <summary>
    /// Торги
    /// </summary>
    public class Bidding
    {
        /// <summary>
        /// Уникальный идентификатор (совпадает с Id торгов old.bankrot.fedresurs.ru)
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Номер торгов (на площадке)
        /// </summary>
        public string TradeNumber { get; set; } = default!;

        /// <summary>
        /// Площадка
        /// </summary>
        public string Platform { get; set; } = default!;

        /// <summary>
        /// Дата объявления о торгах
        /// </summary>
        public DateTime? AnnouncedAt { get; set; }

        /// <summary>
        /// Вид торгов
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
        [ForeignKey("DebtorId")]
        public Subject? Debtor { get; set; }

        /// <summary>
        /// Арбитражный управляющий
        /// </summary>
        public Guid? ArbitrationManagerId { get; set; }
        [ForeignKey("ArbitrationManagerId")]
        public Subject? ArbitrationManager { get; set; }

        /// <summary>
        /// Судебное дело
        /// </summary>
        public Guid? LegalCaseId { get; set; }
        [ForeignKey("LegalCaseId")]
        public LegalCase? LegalCase { get; set; }

        /// <summary>
        /// Идентификатор сообщения о торгах
        /// </summary>
        public Guid BankruptMessageId { get; set; }

        /// <summary>
        /// Порядок ознакомления с имуществом
        /// </summary>
        public string? ViewingProcedure { get; set; }

        /// <summary>
        /// Дата сохранения в БД
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Лот был обогащен (с сайта площадки)
        /// </summary>
        public bool? IsEnriched { get; set; }

        /// <summary>
        /// Дата обогащения лота
        /// </summary>
        public DateTime? EnrichedAt { get; set; }

        /// <summary>
        /// Лоты торгов
        /// </summary>
        public List<Lot> Lots { get; set; } = new();

        /// <summary>
        /// Стейт обогощения торгов (со страницы площадки)
        /// </summary>
        public virtual EnrichmentState? EnrichmentState { get; set; }

        /// <summary>
        /// Признак того, что статусы по всем лотам этих торгов были обновлены до конечного 
        /// ("Завершенные", "Торги отменены", "Торги не состоялись").
        /// </summary>
        public bool IsTradeStatusesFinalized { get; set; }

        /// <summary>
        /// Дата и время последней проверки статуса торгов фоновым сервисом
        /// </summary>
        public DateTime? LastStatusCheckAt { get; set; }
    }
}