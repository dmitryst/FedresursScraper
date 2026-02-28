namespace FedresursScraper.Controllers.Models;

public class LotGeoDto
{
    public Guid Id { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public decimal? StartPrice { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
