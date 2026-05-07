namespace FedresursScraper.UserAds;

public class UserAdDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Price { get; set; }
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Status { get; set; }
    public List<string> ImageUrls { get; set; } = new();
}