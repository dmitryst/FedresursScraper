using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FedresursScraper.Services.SimilarLots;

public class SimilarLotsWorker : BackgroundService
{
    private readonly ILogger<SimilarLotsWorker> _logger;
    private readonly IServiceProvider _serviceProvider;

    // Настройки
    private readonly int _batchSize = 100;
    private readonly TimeSpan _delayBetweenBatches = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _recalculationInterval = TimeSpan.FromHours(24); // Как часто пересчитывать

    public SimilarLotsWorker(ILogger<SimilarLotsWorker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Сервис расчета похожих лотов запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                // Пауза между пачками, чтобы не блокировать пул соединений БД
                await Task.Delay(_delayBetweenBatches, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при расчете похожих лотов.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Пауза при ошибке
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var cutoffTime = DateTime.UtcNow.Subtract(_recalculationInterval);

        // Ищем архивные лоты, для которых:
        // 1. Похожие лоты еще не считались
        // 2. Или считались слишком давно (> 24 часов назад)
        // Используем SQL для эффективной фильтрации
        var finalStatuses = Lot.FinalTradeStatuses;

        var archiveLotsToProcess = await context.Lots
            .AsNoTracking()
            .Where(l => finalStatuses.Contains(l.TradeStatus))
            .Where(l => !context.SimilarLots.Any(sl => sl.SourceLotId == l.Id) ||
                        context.SimilarLots.Where(sl => sl.SourceLotId == l.Id).Max(sl => sl.CalculatedAt) < cutoffTime)
            .Include(l => l.Categories)
            .Take(_batchSize)
            .ToListAsync(stoppingToken);

        if (!archiveLotsToProcess.Any())
        {
            // Если все актуально, спим час
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            return;
        }

        foreach (var sourceLot in archiveLotsToProcess)
        {
            var similarLotRecords = await FindSimilarLotsAsync(context, sourceLot, stoppingToken);

            // Удаляем старые расчеты для этого лота
            var oldRecords = await context.SimilarLots
                .Where(sl => sl.SourceLotId == sourceLot.Id)
                .ToListAsync(stoppingToken);
            context.SimilarLots.RemoveRange(oldRecords);

            // Добавляем новые
            if (similarLotRecords.Any())
            {
                await context.SimilarLots.AddRangeAsync(similarLotRecords, stoppingToken);
            }
        }

        await context.SaveChangesAsync(stoppingToken);
        _logger.LogInformation($"Обработана пачка из {archiveLotsToProcess.Count} лотов.");
    }

    private async Task<List<SimilarLot>> FindSimilarLotsAsync(LotsDbContext context, Lot sourceLot, CancellationToken token)
    {
        var result = new List<SimilarLot>();
        var targetCategories = sourceLot.Categories.Select(c => c.Name).ToList();
        var excludeIds = new HashSet<Guid> { sourceLot.Id };

        // --- ШАГ 1: Жесткий фильтр (Регион + Категория + Цена) ---
        if (sourceLot.StartPrice.HasValue && targetCategories.Any())
        {
            var minPrice = sourceLot.StartPrice.Value * 0.5m; // ±50%
            var maxPrice = sourceLot.StartPrice.Value * 1.5m;

            var strictMatches = await context.Lots
                .AsNoTracking()
                .Where(Lot.IsActiveExpression)
                .Where(l => l.PropertyRegionCode == sourceLot.PropertyRegionCode)
                .Where(l => l.StartPrice >= minPrice && l.StartPrice <= maxPrice)
                .Where(l => l.Categories.Any(c => targetCategories.Contains(c.Name)))
                .Where(l => l.Id != sourceLot.Id)
                .OrderByDescending(l => l.CreatedAt)
                .Take(4)
                .Select(l => l.Id)
                .ToListAsync(token);

            AddResults(result, strictMatches, sourceLot.Id, "Strict", excludeIds);
        }

        // --- ШАГ 2: Fallback (Только категория по всей РФ) ---
        // Если жесткий фильтр дал меньше 4 результатов
        if (result.Count < 4 && targetCategories.Any())
        {
            var limit = 4 - result.Count;
            var fallbackMatches = await context.Lots
                .AsNoTracking()
                .Where(Lot.IsActiveExpression)
                .Where(l => l.Categories.Any(c => targetCategories.Contains(c.Name)))
                .Where(l => !excludeIds.Contains(l.Id)) // Исключаем уже найденные и сам исходный лот
                .OrderByDescending(l => l.CreatedAt) // Берем самые свежие
                .Take(limit)
                .Select(l => l.Id)
                .ToListAsync(token);

            AddResults(result, fallbackMatches, sourceLot.Id, "CategoryFallback", excludeIds);
        }

        return result;
    }

    private void AddResults(List<SimilarLot> result, List<Guid> foundIds, Guid sourceId, string algo, HashSet<Guid> excludeIds)
    {
        foreach (var targetId in foundIds)
        {
            result.Add(new SimilarLot
            {
                SourceLotId = sourceId,
                TargetLotId = targetId,
                Algorithm = algo,
                Rank = result.Count + 1,
                CalculatedAt = DateTime.UtcNow
            });
            excludeIds.Add(targetId);
        }
    }
}