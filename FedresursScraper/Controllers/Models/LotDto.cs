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
    public IEnumerable<PriceScheduleDto> PriceSchedules { get; set; } = new List<PriceScheduleDto>();
    public List<string> Images { get; set; } = new();
    public List<LotDocumentDto> Documents { get; set; } = new();
    /// <summary>
    /// Название региона местонахождения имущества (если указан в описании)
    /// </summary>
    public string? PropertyRegionName { get; set; }

    /// <summary>
    /// Полный адрес местонахождения имущества (если указан в описании)
    /// </summary>
    public string? PropertyFullAddress { get; set; }

    /// <summary>
    /// Рыночная стоимость объекта (оценка ИИ)
    /// </summary>
    public decimal? MarketValue { get; set; }

    /// <summary>
    /// Короткий инвестиционный комментарий (2–3 предложения): логика marketValue, риски, потенциал.
    /// </summary>
    public string? InvestmentSummary { get; set; }
}
