namespace Lots.Data.Models;

public class LotGeoResult
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public decimal? StartPrice { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}