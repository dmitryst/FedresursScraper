namespace FedresursScraper.Services;

/// <summary>
/// Правила числовой оценки лотов: какие категории оцениваются и пост-валидация ответа LLM.
/// </summary>
public static class LotPriceEvaluationRules
{
    public const string NotEvaluableConfidence = "not_evaluable";

    /// <summary>
    /// Категории, для которых автоматическая числовая оценка не проводится.
    /// </summary>
    public static readonly HashSet<string> NonEvaluableCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Доли в уставном капитале",
        "Ценные бумаги",
        "Готовый бизнес",
        "Имущественный комплекс",
        "Дебиторская задолженность",
    };

    public const string ClassificationPriceInstructions =
        "7. Рыночная стоимость (marketValueMin/Max) — ТОЛЬКО для стандартизируемых активов: " +
        "недвижимость с площадью/адресом/кадастром, транспорт с маркой/моделью/годом, оборудование с конкретной моделью.\n" +
        "   Для «Доли в уставном капитале», «Ценные бумаги», «Готовый бизнес», «Имущественный комплекс», «Дебиторская задолженность» — " +
        "всегда marketValueMin = null, marketValueMax = null, priceConfidence = \"not_evaluable\".\n" +
        "   Начальная цена торгов — цена организатора, НЕ рыночная оценка. Не строй диапазон вокруг неё.\n" +
        "   Оценивай независимо по рынку; если данных недостаточно — null и priceConfidence = \"low\".\n" +
        "   priceConfidence: \"high\" — достаточно конкретики; \"medium\" — часть данных отсутствует; " +
        "\"low\" — описание слишком общее; \"not_evaluable\" — тип актива не поддаётся автооценке.\n\n";

    /// <summary>
    /// Проверяет, можно ли показывать числовую оценку для набора категорий.
    /// </summary>
    public static bool IsCategoryPriceEvaluable(IEnumerable<string>? categories)
    {
        if (categories == null)
            return true;

        return !categories.Any(c => !string.IsNullOrWhiteSpace(c) && NonEvaluableCategories.Contains(c.Trim()));
    }

    /// <summary>
    /// Корректирует результат классификации: обнуляет якорные или неоцениваемые диапазоны.
    /// </summary>
    public static void SanitizeClassificationResult(LotClassificationResult result, decimal? startPrice)
    {
        if (result == null)
            return;

        if (!result.HasPropertyDescription)
        {
            ClearPriceEstimate(result, NotEvaluableConfidence);
            return;
        }

        if (!IsCategoryPriceEvaluable(result.Categories))
        {
            ClearPriceEstimate(result, NotEvaluableConfidence);
            return;
        }

        if (IsLikelyAnchoredToStartPrice(startPrice, result.MarketValueMin, result.MarketValueMax))
        {
            ClearPriceEstimate(result, "low");
        }
    }

    public static void ClearPriceEstimate(LotClassificationResult result, string confidence)
    {
        result.MarketValueMin = null;
        result.MarketValueMax = null;
        result.PriceConfidence = confidence;
    }

    /// <summary>
    /// Диапазон «прилип» к начальной цене торгов (типичный паттерн LLM при якорении).
    /// </summary>
    public static bool IsLikelyAnchoredToStartPrice(decimal? startPrice, decimal? min, decimal? max)
    {
        if (!startPrice.HasValue || startPrice.Value <= 0 || !min.HasValue || !max.HasValue)
            return false;

        var sp = startPrice.Value;
        return min.Value >= sp * 0.65m && max.Value <= sp * 1.35m;
    }
}
