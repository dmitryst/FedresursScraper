namespace Lots.Application.Services.VehicleFilters;

public interface IVehicleFilterOptionsCache
{
    VehicleFilterOptions Get();
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
