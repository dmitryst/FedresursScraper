using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;

namespace FedresursScraper.Services;

/// <summary>
/// Фоновый процесс реализует логику "взять 100 неклассифицированных лотов из БД — 
/// обработать (наполнить очередь) — ждать завершения — взять следующие".
/// </summary>
public class LotRecoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotRecoveryService> _logger;
    private readonly IConfiguration _configuration;
    private const int BatchSize = 10;
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
                    // Берем лоты без категорий и тайтла, но с описанием
                    .Where(l => !l.Categories.Any() &&
                        string.IsNullOrEmpty(l.Title) &&
                        !string.IsNullOrEmpty(l.Description))
                    // DeepSeek может вернуть пустой массив categories. Чтобы избежать зациклинности,
                    // исключаем те, по которым была хотя бы одна успешная классификация
                    .Where(l => !dbContext.LotAuditEvents.Any(e =>
                        e.LotId == l.Id &&
                        e.EventType == "Classification" &&
                        e.Status == "Success"))
                    // Не было никакой активности за последний час
                    .Where(l => !dbContext.LotAuditEvents.Any(e =>
                        e.LotId == l.Id && e.EventType == "Classification" &&
                        e.Timestamp > retryDelay))
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

                // Батчевая классификация выполняется только если набралось достаточно лотов
                if (lotsToProcess.Count < BatchSize)
                {
                    _logger.LogInformation("Найдено {Count} лотов, но требуется минимум {BatchSize} для батчевой классификации. Ждем 1 час.", 
                        lotsToProcess.Count, BatchSize);
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Найдено {Count} лотов для восстановления классификации.", lotsToProcess.Count);

                var lotIds = lotsToProcess.Select(x => x.Id).ToList();

                // Выполняем батчевую классификацию напрямую (без очереди)
                await classificationManager.ClassifyLotsBatchAsync(lotIds, "Recovery");

                _logger.LogInformation("Батчевая классификация для {Count} лотов завершена.", lotIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле восстановления лотов.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
