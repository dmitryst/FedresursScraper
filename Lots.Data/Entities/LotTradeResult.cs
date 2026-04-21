using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

public class LotTradeResult
{
    public Guid Id { get; set; }

    public Guid BiddingId { get; set; }
    [ForeignKey("BiddingId")]
    public Bidding Bidding { get; set; } = default!;

    /// <summary>
    /// Идентификатор сообщения на Федресурсе (из URL), чтобы не парсить дубли.
    /// </summary>
    public Guid MessageId { get; set; }

    public string LotNumber { get; set; } = default!;

    /// <summary>
    /// "Торги не состоялись" или "Результаты торгов"
    /// </summary>
    public string EventType { get; set; } = default!;

    public DateTime EventDate { get; set; }

    // Поля для несостоявшихся торгов
    public string? Reason { get; set; }

    // Поля для состоявшихся торгов
    public decimal? FinalPrice { get; set; }
    public string? WinnerName { get; set; }
    public string? WinnerInn { get; set; }

    /// <summary>
    /// Официальный статус лота из сообщения (например, "Торги не состоялись")
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Обоснование принятого решения
    /// </summary>
    public string? DecisionJustification { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Флаг для будущего API-экспорта с локальной машины на прод
    /// </summary>
    public bool IsExportedToProd { get; set; } = false;
}