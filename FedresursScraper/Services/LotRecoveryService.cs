using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;

/// <summary>
/// Фоновый процесс реализует логику "взять 100 неклассифицированных лотов из БД — 
/// обработать (наполнить очередь) — ждать завершения — взять следующие".
/// </summary>
public class LotRecoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotRecoveryService> _logger;
    private const int BatchSize = 1;

    public LotRecoveryService(
        IServiceProvider serviceProvider,
        ILogger<LotRecoveryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LotRecoveryService запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                var classificationManager = scope.ServiceProvider.GetRequiredService<IClassificationManager>();

                // Берем лоты без категорий (неклассифицированные)
                // Исключаем те, которые уже в процессе (есть событие Start без результата за последний час)
                var thresholdTime = DateTime.UtcNow.AddHours(-1);
                
                // Для простоты берем просто без категорий. Аудит защитит от бесконечного цикла, если будем проверять статус.
                // Чтобы не брать те, которые только что упали или в работе, можно джойнить с аудитом, но для начала простой вариант:
                var lotsToProcess = await dbContext.Lots
                    .Where(l => !l.Categories.Any() && !string.IsNullOrEmpty(l.Description))
                    .OrderBy(l => l.Id) // Важно для детерминированности
                    .Take(BatchSize)
                    .Select(l => new { l.Id, l.Description })
                    .ToListAsync(stoppingToken);

                if (lotsToProcess.Count == 0)
                {
                    _logger.LogInformation("Нет неклассифицированных лотов. Ждем 1 час.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Найдено {Count} лотов для восстановления классификации.", lotsToProcess.Count);

                // Фильтруем те, которые мы уже пытались обработать совсем недавно (чтобы не спамить API, если денег нет)
                // Это можно сделать запросом в БД, но для 100 штук можно и в памяти, если нагрузка небольшая,
                // либо усложнить SQL запрос выше. 
                // Рекомендую добавить проверку: если был FAILURE в последние 30 минут, пропускаем.

                var lotIds = lotsToProcess.Select(x => x.Id).ToList();
                var startTime = DateTime.UtcNow;

                // Ставим в очередь
                foreach (var lot in lotsToProcess)
                {
                    await classificationManager.EnqueueClassificationAsync(lot.Id, lot.Description!, "Recovery");
                }

                // Ждем, пока эта пачка обработается
                // Мы не можем знать наверняка, когда очередь дойдет до них, но мы можем поллить таблицу аудита
                // Проверяем, появились ли события Success или Failure для этих ID после startTime.
                
                bool batchCompleted = false;
                while (!batchCompleted && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

                    using var monitorScope = _serviceProvider.CreateScope();
                    var monitorDb = monitorScope.ServiceProvider.GetRequiredService<LotsDbContext>();

                    var processedCount = await monitorDb.LotAuditEvents
                        .Where(e => lotIds.Contains(e.LotId) 
                                    && e.Timestamp >= startTime 
                                    && (e.Status == "Success" || e.Status == "Failure"))
                        .CountAsync(stoppingToken);

                    if (processedCount >= lotIds.Count)
                    {
                        batchCompleted = true;
                        _logger.LogInformation("Пачка из {Count} лотов обработана.", lotIds.Count);
                    }
                    else
                    {
                        // Таймаут защиты от зависания (например, 30 минут на пачку)
                        if ((DateTime.UtcNow - startTime).TotalMinutes > 30)
                        {
                            _logger.LogWarning("Таймаут ожидания обработки пачки лотов.");
                            break; 
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле восстановления лотов.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
