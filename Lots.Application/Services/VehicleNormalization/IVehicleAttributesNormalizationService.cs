namespace Lots.Application.Services.VehicleNormalization;

public interface IVehicleAttributesNormalizationService
{
    /// <summary>
    /// Нормализует brand/model в словаре атрибутов. Возвращает true, если значения изменились.
    /// </summary>
    bool NormalizeAttributes(Dictionary<string, string> attributes);
}
