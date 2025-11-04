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
        /// Лоты торгов
        /// </summary>
        public List<Lot> Lots { get; set; } = new();
    }
}