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
    private readonly IConfiguration _configuration;
    private const int BatchSize = 20;
    private const int MaxAttempts = 1; // Лимит попыток перед отправкой на "ручной разбор"

    public LotRecoveryService(
        IServiceProvider serviceProvider,
        ILogger<LotRecoveryService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LotRecoveryService запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_configuration.GetValue<bool>("Features:EnableLotRecovery", true))
            {
                _logger.LogInformation("LotRecoveryService отключен в конфигурации. Сплю 1 минуту.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                var classificationManager = scope.ServiceProvider.GetRequiredService<IClassificationManager>();

                var retryDelay = DateTime.UtcNow.AddHours(-1); // Не трогать лот, если пробовали за последний час

                var lotsToProcess = await dbContext.Lots
                    // Берем лоты без категорий
                    .Where(l => !l.Categories.Any() && !string.IsNullOrEmpty(l.Description))
                    // DeepSeek может вернуть пустой массив categories. Чтобы избежать зациклинности,
                    // исключаем те, по которым была хотя бы одна успешная классификация
                    .Where(l => !dbContext.LotAuditEvents.Any(e =>
                        e.LotId == l.Id &&
                        e.EventType == "Classification" &&
                        e.Status == "Success"))
                    // Исключаем лоты, у которых уже накопилось слишком много неудачных попыток
                    // Такие лоты потом можно найти отдельным SQL-запросом для ручного разбора
                    .Where(l => dbContext.LotAuditEvents
                        .Count(e => e.LotId == l.Id && e.EventType == "Classification" && e.Status == "Failure") < MaxAttempts)
                    .OrderBy(l => l.Id) // Важно для детерминированности
                    .Take(BatchSize)
                    .Select(l => new { l.Id, l.Description })
                    .ToListAsync(stoppingToken);

                if (lotsToProcess.Count == 0)
                {
                    _logger.LogInformation("Нет лотов на классификацию. Ждем 1 час.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Найдено {Count} лотов для восстановления классификации.", lotsToProcess.Count);

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
                                    && (e.Status == "Success" || e.Status == "Failure" || e.Status == "Skipped"))
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
