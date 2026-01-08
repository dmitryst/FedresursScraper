namespace FedresursScraper.Controllers.Models;

public class LotDto
{
    public Guid Id { get; set; }
    public int PublicId { get; set; }
    public string? LotNumber { get; set; }
    public decimal? StartPrice { get; set; }
    public decimal? Step { get; set; }
    public decimal? Deposit { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ViewingProcedure { get; set; }
    public DateTime CreatedAt { get; set; }
    public double[]? Coordinates { get; set; }
    public BiddingDto Bidding { get; set; } = new();
    public List<CategoryDto> Categories { get; set; } = new();
}
