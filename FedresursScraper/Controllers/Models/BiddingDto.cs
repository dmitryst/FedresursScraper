namespace FedresursScraper.Controllers.Models;

public class BiddingDto
{
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Площадка
    /// </summary>
    public string Platform { get; set; } = default!;
    public string? BidAcceptancePeriod { get; set; }
    public string? ViewingProcedure { get; set; }
}
