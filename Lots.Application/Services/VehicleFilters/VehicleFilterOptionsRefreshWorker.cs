using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lots.Application.Services.VehicleFilters;

public class VehicleFilterOptionsRefreshWorker : BackgroundService
{
    private readonly IVehicleFilterOptionsCache _cache;
    private readonly ILogger<VehicleFilterOptionsRefreshWorker> _logger;
    private readonly TimeSpan _refreshInterval;

    public VehicleFilterOptionsRefreshWorker(
        IVehicleFilterOptionsCache cache,
        IOptions<VehicleFilterOptionsCacheSettings> settings,
        ILogger<VehicleFilterOptionsRefreshWorker> logger)
    {
        _cache = cache;
        _logger = logger;

        var minutes = settings.Value.RefreshIntervalMinutes;
        if (minutes < 1)
        {
            minutes = 30;
        }

        _refreshInterval = TimeSpan.FromMinutes(minutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Фоновое обновление кэша марок/моделей запущено (интервал: {Minutes} мин).",
            _refreshInterval.TotalMinutes);

        await RefreshSafeAsync(stoppingToken);

        using var timer = new PeriodicTimer(_refreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshSafeAsync(stoppingToken);
        }
    }

    private async Task RefreshSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RefreshAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка при обновлении кэша марок/моделей.");
        }
    }
}
