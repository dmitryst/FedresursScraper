namespace Lots.Application.Services.VehicleFilters;

public class VehicleFilterOptions
{
    public List<string> Brands { get; init; } = [];
    public Dictionary<string, List<string>> ModelsByBrand { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CachedAtUtc { get; init; }
}

public class VehicleFilterOptionsCacheSettings
{
    public int RefreshIntervalMinutes { get; set; } = 30;
}
