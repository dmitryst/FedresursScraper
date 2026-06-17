using Lots.Application.Services.VehicleFilters;
using Lots.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lots.Application.Services.VehicleFilters;

public class VehicleFilterOptionsCache : IVehicleFilterOptionsCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VehicleFilterOptionsCache> _logger;
    private readonly object _lock = new();
    private VehicleFilterOptions _cached = new();

    public VehicleFilterOptionsCache(
        IServiceScopeFactory scopeFactory,
        ILogger<VehicleFilterOptionsCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public VehicleFilterOptions Get()
    {
        lock (_lock)
        {
            return _cached;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var normalizer = scope.ServiceProvider.GetRequiredService<VehicleNormalization.IVehicleBrandModelNormalizer>();

        var snapshot = normalizer.GetCatalogOptions();

        lock (_lock)
        {
            _cached = snapshot;
        }

        await Task.CompletedTask;

        _logger.LogInformation(
            "Кэш марок/моделей обновлён из справочника: {BrandCount} марок.",
            snapshot.Brands.Count);
    }
}
