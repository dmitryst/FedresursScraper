using System.Collections.Generic;
using System.Text.Json.Serialization;

public class LotClassificationResult
{
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = default!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = default!;

    [JsonPropertyName("isSharedOwnership")]
    public bool IsSharedOwnership { get; set; }

    [JsonPropertyName("suggestedCategory")]
    public string? SuggestedCategory { get; set; }

    /// <summary>
    /// Код региона местонахождения имущества (если указан в описании)
    /// </summary>
    [JsonPropertyName("propertyRegionCode")]
    public string? PropertyRegionCode { get; set; }

    /// <summary>
    /// Название региона местонахождения имущества (если указан в описании)
    /// </summary>
    [JsonPropertyName("propertyRegionName")]
    public string? PropertyRegionName { get; set; }

    /// <summary>
    /// Полный адрес местонахождения имущества (если указан в описании)
    /// </summary>
    [JsonPropertyName("propertyFullAddress")]
    public string? PropertyFullAddress { get; set; }

    /// <summary>
    /// Рыночная стоимость объекта (оценка ИИ)
    /// </summary>
    [JsonPropertyName("marketValue")]
    public decimal? MarketValue { get; set; }

    /// <summary>
    /// Короткий инвестиционный комментарий (2–3 предложения): логика marketValue, риски, потенциал.
    /// </summary>
    [JsonPropertyName("investmentSummary")]
    public string? InvestmentSummary { get; set; }
}