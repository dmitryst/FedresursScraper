using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Результат классификации и анализа лота через LLM.
/// </summary>
public class LotClassificationResult
{
    /// <summary>
    /// Список категорий, к которым модель отнесла лот.
    /// </summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = default!;

    /// <summary>
    /// Сформированное красивое название лота.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = default!;

    /// <summary>
    /// Флаг долевой собственности (true, если продается доля).
    /// </summary>
    [JsonPropertyName("isSharedOwnership")]
    public bool IsSharedOwnership { get; set; }

    /// <summary>
    /// Предложенная категория (если ни одна из списка не подошла).
    /// </summary>
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
    /// Нижняя граница рыночной стоимости (ликвидационная цена / быстрая продажа).
    /// </summary>
    [JsonPropertyName("marketValueMin")]
    public decimal? MarketValueMin { get; set; }

    /// <summary>
    /// Верхняя граница рыночной стоимости (оптимистичная / рыночная цена).
    /// </summary>
    [JsonPropertyName("marketValueMax")]
    public decimal? MarketValueMax { get; set; }

    /// <summary>
    /// Уровень уверенности модели в оценке: "low", "medium", "high".
    /// </summary>
    [JsonPropertyName("priceConfidence")]
    public string? PriceConfidence { get; set; }

    /// <summary>
    /// Короткий инвестиционный комментарий (2–3 предложения): логика marketValue, риски, потенциал.
    /// </summary>
    [JsonPropertyName("investmentSummary")]
    public string? InvestmentSummary { get; set; }
}