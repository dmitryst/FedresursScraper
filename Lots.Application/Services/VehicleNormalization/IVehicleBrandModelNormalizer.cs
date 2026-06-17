using Lots.Application.Services.VehicleFilters;

namespace Lots.Application.Services.VehicleNormalization;

public interface IVehicleBrandModelNormalizer
{
    (string? Brand, string? Model, bool BrandMatched, bool ModelMatched) Normalize(
        string? brand,
        string? model);

    /// <summary>
    /// Марки и модели из справочника для выпадающих списков фильтра.
    /// </summary>
    VehicleFilterOptions GetCatalogOptions();
}
