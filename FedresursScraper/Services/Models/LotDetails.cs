namespace FedresursScraper.Services.Models;

/// <summary>
/// DTO для хранения результата парсинга лота
/// </summary>
public class LotDetails
{
    public string BiddingType { get; set; } = default!;
    public string? BidAcceptancePeriod { get; set; }
    public string? ViewingProcedure { get; set; }
    public string? Description { get; set; }
    public decimal? StartPrice { get; set; }
    public decimal? Step { get; set; }
    public decimal? Deposit { get; set; }
    public DateTime? AnnouncedAt { get; set; }
    public Guid BankruptMessageId { get; set; }
    public List<string>? CadastralNumbers { get; set; }
}