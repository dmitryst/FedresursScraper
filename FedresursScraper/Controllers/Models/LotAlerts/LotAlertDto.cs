namespace FedresursScraper.Models.LotAlerts;

public class LotAlertDto
{
    public Guid Id { get; set; }
    public string[]? RegionCodes { get; set; }
    public string[]? Categories { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? BiddingType { get; set; }
    public bool? IsSharedOwnership { get; set; }
    public string DeliveryTimeStr { get; set; } = "09:00"; 
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

