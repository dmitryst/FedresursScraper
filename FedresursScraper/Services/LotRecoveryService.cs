using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;

namespace FedresursScraper.Services;

/// <summary>
/// Фоновый процесс реализует логику "взять 10 неклассифицированных лотов из БД — 
/// обработать (наполнить очередь) — ждать завершения — взять следующие".
/// </summary>
public class LotRecoveryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotRecoveryService> _logger;
    private readonly IConfiguration _configuration;

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
            if (!_configuration.GetValue("BackgroundServices:LotRecoveryService:Enabled", false))
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
                var indexNowService = scope.ServiceProvider.GetService<IIndexNowService>();

                var retryDelay = DateTime.UtcNow.AddHours(-1); // Не трогать лот, если пробовали за последний час

                var batchSize = _configuration.GetValue("BackgroundServices:LotRecoveryService:BatchSize", 10);
                var maxAttempts = _configuration.GetValue("BackgroundServices:LotRecoveryService:MaxAttempts", 1);

                var lotsToProcess = await dbContext.Lots
                    // временно ставим условие > 50000, чтобы не делать классификацию слишком старых лотов
                    .Where(l => l.PublicId > 50000)
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
                        .Count(e => e.LotId == l.Id && e.EventType == "Classification" && e.Status == "Failure") < maxAttempts)
                    .OrderBy(l => l.Id) // Важно для детерминированности
                    .Take(batchSize)
                    .Select(l => new { l.Id, l.Description })
                    .ToListAsync(stoppingToken);

                if (lotsToProcess.Count == 0)
                {
                    _logger.LogInformation("Нет лотов на классификацию. Ждем 20 минут.");
                    await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
                    continue;
                }

                // Батчевая классификация выполняется только если набралось достаточно лотов
                if (lotsToProcess.Count < batchSize)
                {
                    _logger.LogInformation("Найдено {Count} лотов, но требуется минимум {BatchSize} для батчевой классификации. Ждем 20 минут.",
                        lotsToProcess.Count, batchSize);
                    await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Найдено {Count} лотов для восстановления классификации.", lotsToProcess.Count);

                var lotIds = lotsToProcess.Select(x => x.Id).ToList();

                // Выполняем батчевую классификацию напрямую (без очереди)
                await classificationManager.ClassifyLotsBatchAsync(lotIds, "Recovery");

                _logger.LogInformation("Батчевая классификация для {Count} лотов завершена.", lotIds.Count);

                // Отправляем url лотов в IndexNow
                if (indexNowService != null)
                {
                    await SubmitToIndexNowAsync(dbContext, indexNowService, lotIds, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле восстановления лотов.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Формирует URL для обновленных лотов и отправляет их в IndexNow.
    /// </summary>
    private async Task SubmitToIndexNowAsync(
        LotsDbContext dbContext,
        IIndexNowService indexNowService,
        List<Guid> lotIds,
        CancellationToken stoppingToken)
    {
        try
        {
            // Получаем лоты для обновления
            var updatedLots = await dbContext.Lots
                .Where(l => lotIds.Contains(l.Id))
                .ToListAsync(stoppingToken);

            if (!updatedLots.Any())
            {
                _logger.LogWarning("IndexNow: Не удалось найти лоты после классификации.");
                return;
            }

            var urlsToSubmit = new List<string>();
            var baseUrl = _configuration["App:BaseUrl"] ?? "https://s-lot.ru";
            var lotsToUpdate = false;

            foreach (var lot in updatedLots)
            {
                if (string.IsNullOrEmpty(lot.Slug))
                {
                    var textForSlug = lot.Title ?? lot.Description;

                    if (string.IsNullOrEmpty(lot.Title))
                    {
                        _logger.LogWarning("Внимание: Лот {LotId} остался без Title после классификации! Slug сгенерирован из Description.", lot.Id);
                    }

                    // Генерируем Slug
                    lot.Slug = SlugHelper.GenerateSlug(textForSlug!);
                    lotsToUpdate = true;
                }

                // Формируем URL, используя сохраненный Slug
                var url = $"{baseUrl}/lot/{lot.Slug}-{lot.PublicId}";
                urlsToSubmit.Add(url);
            }

            // Сохраняем Slug в БД, если были изменения
            if (lotsToUpdate)
            {
                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Slug сгенерированы и сохранены для {Count} лотов.",
                    updatedLots.Count(l => !string.IsNullOrEmpty(l.Slug)));
            }

            // Отправляем в IndexNow
            if (urlsToSubmit.Any())
            {
                await indexNowService.SubmitUrlsAsync(urlsToSubmit);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации Slug или отправке в IndexNow.");
        }
    }
}
