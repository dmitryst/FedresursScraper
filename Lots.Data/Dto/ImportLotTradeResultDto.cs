namespace Lots.Data.Dto;

/// <summary>
/// DTO для переноса данных из локального PostgreSQL в боевой.
/// </summary>
public class ImportLotTradeResultDto
{
    public Guid BiddingId { get; set; }
    public Guid MessageId { get; set; }
    public string LotNumber { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public DateTime EventDate { get; set; }
    public string? Reason { get; set; }
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
}