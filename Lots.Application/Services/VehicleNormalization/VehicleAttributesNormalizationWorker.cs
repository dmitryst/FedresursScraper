using Lots.Application.Services.VehicleFilters;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lots.Application.Services.VehicleNormalization;

public class VehicleAttributesNormalizationWorker : BackgroundService
{
    private const string VehicleCategory = "Легковой автомобиль";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVehicleFilterOptionsCache _filterOptionsCache;
    private readonly ILogger<VehicleAttributesNormalizationWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;

    public VehicleAttributesNormalizationWorker(
        IServiceScopeFactory scopeFactory,
        IVehicleFilterOptionsCache filterOptionsCache,
        IOptions<VehicleNormalizationSettings> settings,
        ILogger<VehicleAttributesNormalizationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _filterOptionsCache = filterOptionsCache;
        _logger = logger;

        var config = settings.Value;
        _batchSize = config.BatchSize > 0 ? config.BatchSize : 100;
        _interval = TimeSpan.FromMinutes(config.IntervalMinutes > 0 ? config.IntervalMinutes : 60);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Фоновая нормализация марок/моделей запущена (батч: {BatchSize}, интервал: {Minutes} мин).",
            _batchSize,
            _interval.TotalMinutes);

        await ProcessSafeAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessSafeAsync(stoppingToken);
        }
    }

    private async Task ProcessSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var totalChanged = await NormalizePendingLotsAsync(cancellationToken);

            if (totalChanged > 0)
            {
                await _filterOptionsCache.RefreshAsync(cancellationToken);
                _logger.LogInformation(
                    "Нормализация завершена: обновлено {Count} лотов, кэш фильтров перестроен.",
                    totalChanged);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка при нормализации марок/моделей.");
        }
    }

    private async Task<int> NormalizePendingLotsAsync(CancellationToken cancellationToken)
    {
        var totalChanged = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
            var normalizationService = scope.ServiceProvider.GetRequiredService<IVehicleAttributesNormalizationService>();

            var lots = await dbContext.Lots
                .Where(Lot.IsActiveExpression)
                .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
                .Where(l => l.Attributes != null && EF.Functions.JsonExists(l.Attributes, "brand"))
                .Where(l => l.Attributes == null || !EF.Functions.JsonExists(l.Attributes, "_brand_matched"))
                .OrderBy(l => l.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(cancellationToken);

            if (lots.Count == 0)
            {
                break;
            }

            var batchChanged = 0;

            foreach (var lot in lots)
            {
                var attributes = lot.Attributes != null
                    ? new Dictionary<string, string>(lot.Attributes)
                    : new Dictionary<string, string>();

                if (normalizationService.NormalizeAttributes(attributes))
                {
                    batchChanged++;
                }

                lot.Attributes = attributes;
            }

            if (batchChanged > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Все лоты уже в canonical-виде, но флаг не был проставлен
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            totalChanged += batchChanged;

            if (lots.Count < _batchSize)
            {
                break;
            }
        }

        return totalChanged;
    }
}
