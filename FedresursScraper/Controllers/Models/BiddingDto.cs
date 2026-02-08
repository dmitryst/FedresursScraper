namespace FedresursScraper.Controllers.Models;

/// <summary>
/// Торги
/// </summary>
public class BiddingDto
{
    /// <summary>
    /// Вид торгов
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Площадка
    /// </summary>
    public string Platform { get; set; } = default!;

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
    /// Порядок ознакомления с имуществом
    /// </summary>
    public string? ViewingProcedure { get; set; }

    /// <summary>
    /// Арбитражный управляющий
    /// </summary>
    public ArbitrationManagerDto? ArbitrationManager { get; set; }
}
