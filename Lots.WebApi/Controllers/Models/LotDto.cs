using FedresursScraper.Clients;

namespace FedresursScraper.Controllers.Models;

public class LotDto
{
    public Guid Id { get; set; }
    public int PublicId { get; set; }
    public string? LotNumber { get; set; }
    public decimal? StartPrice { get; set; }
    public decimal? Step { get; set; }
    public decimal? Deposit { get; set; }
    public string? TradeStatus { get; set; }
    /// <summary>
    /// Причина текущего статуса торгов (из последнего сообщения Федресурса).
    /// </summary>
    public string? TradeStatusReason { get; set; }
    public decimal? FinalPrice { get; set; }
    public string? WinnerName { get; set; }
    public string? WinnerInn { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
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
    /// Нижняя граница рыночной стоимости (ликвидационная цена / быстрая продажа).
    /// </summary>
    public decimal? MarketValueMin { get; set; }

    /// <summary>
    /// Верхняя граница рыночной стоимости (оптимистичная / рыночная цена).
    /// </summary>
    public decimal? MarketValueMax { get; set; }

    /// <summary>
    /// Уровень уверенности модели в оценке: "low", "medium", "high".
    /// </summary>
    public string? PriceConfidence { get; set; }

    /// <summary>
    /// Короткий инвестиционный комментарий (2–3 предложения): логика marketValue, риски, потенциал.
    /// </summary>
    public string? InvestmentSummary { get; set; }

    /// <summary>
    /// Детальный анализ (Deep Evaluation)
    /// </summary>
    public string? ReasoningText { get; set; }

    /// <summary>
    /// Является ли ReasoningText тизером (обрезанным текстом)
    /// </summary>
    public bool IsReasoningTextTeaser { get; set; }

    /// <summary>
    /// Оценка ликвидности (из Deep Evaluation)
    /// </summary>
    public int? LiquidityScore { get; set; }

    /// <summary>
    /// Данные из Росреестра по конкретному кадастровому номеру
    /// </summary>
    public List<CadastralItemDto>? CadastralInfos { get; set; }

    /// <summary>
    /// Похожие лоты (для архивных лотов)
    /// </summary>
    public List<SimilarLotDto> SimilarLots { get; set; } = new();

    /// <summary>
    /// Активные лоты с такими же кадастровыми номерами
    /// </summary>
    public List<SimilarLotDto> SameCadastralLots { get; set; } = new();

    /// <summary>
    /// Динамические атрибуты лота
    /// </summary>
    public Dictionary<string, string>? Attributes { get; set; }

    /// <summary>
    /// Описание не содержит информации об имуществе — требуется ручная доработка.
    /// </summary>
    public bool NeedsDescriptionReview { get; set; }
}

public class SimilarLotDto
{
    public Guid Id { get; set; }
    public int PublicId { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public decimal? StartPrice { get; set; }
    public string? ImageUrl { get; set; }
}
