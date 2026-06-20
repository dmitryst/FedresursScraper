using FedresursScraper.Controllers.Models;

namespace FedresursScraper.Controllers.Utils;

/// <summary>
/// Временное ограничение видимости экспресс-оценки (результат классификации).
/// </summary>
public static class LotDtoAiEvaluationAccess
{
    public static void ApplyQuickEvaluationVisibility(LotDto dto, bool visible)
    {
        if (visible)
            return;

        dto.MarketValue = null;
        dto.MarketValueMin = null;
        dto.MarketValueMax = null;
        dto.PriceConfidence = null;
        dto.InvestmentSummary = null;
    }

    public static void ApplyQuickEvaluationVisibility(IEnumerable<LotDto> dtos, bool visible)
    {
        if (visible)
            return;

        foreach (var dto in dtos)
        {
            ApplyQuickEvaluationVisibility(dto, visible: false);
        }
    }
}
