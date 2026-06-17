namespace Lots.Application.Services.VehicleNormalization;

public class VehicleCatalog
{
    public List<VehicleBrandEntry> Brands { get; set; } = [];
}

public class VehicleBrandEntry
{
    public string Canonical { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public List<VehicleModelEntry> Models { get; set; } = [];
}

public class VehicleModelEntry
{
    public string Canonical { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
}

public class VehicleCatalogSettings
{
    /// <summary>
    /// Путь к JSON-справочнику. Если не задан — используется Data/vehicle-catalog.json рядом с приложением.
    /// </summary>
    public string? CatalogPath { get; set; }
}

public class VehicleNormalizationSettings
{
    public bool BackfillEnabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int IntervalMinutes { get; set; } = 60;
}
