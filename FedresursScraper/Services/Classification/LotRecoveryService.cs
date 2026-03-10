using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using Lots.Data.Entities;

namespace FedresursScraper.Services;

/// <summary>
/// Фоновый процесс восстановления неклассифицированных лотов.
/// Реализует паттерн надежной очереди через инфраструктурную таблицу состояний.
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

    /// <summary>
    /// Основной цикл выполнения фоновой задачи.
    /// Пополняет очередь состояний, бронирует задачи и передает их на классификацию.
    /// </summary>
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
                var now = DateTime.UtcNow;

                // Ленивое пополнение очереди: находим "осиротевшие" лоты и добавляем их в таблицу состояний.
                // ON CONFLICT DO NOTHING гарантирует безопасность при конкурентных запусках.
                var sqlInsert = $@"
                    INSERT INTO ""LotClassificationStates"" (""LotId"", ""Status"", ""Attempts"", ""NextAttemptAt"")
                    SELECT l.""Id"", {(int)ClassificationStatus.Pending}, 0, NULL
                    FROM ""Lots"" l
                    LEFT JOIN ""LotClassificationStates"" s ON l.""Id"" = s.""LotId""
                    WHERE l.""PublicId"" > 50000 
                      AND (l.""Title"" IS NULL OR l.""Title"" = '')
                      AND l.""Description"" IS NOT NULL
                      AND s.""LotId"" IS NULL
                    LIMIT {batchSize * 2}
                    ON CONFLICT (""LotId"") DO NOTHING;";

                await dbContext.Database.ExecuteSqlRawAsync(sqlInsert, stoppingToken);

                // Быстрая выборка идентификаторов задач из инфраструктурной таблицы очереди
                var lotIdsToProcess = await dbContext.LotClassificationStates
                    .Where(s =>
                        s.Status == ClassificationStatus.Pending ||
                        (s.Status == ClassificationStatus.Failed && s.Attempts < maxAttempts) ||
                        (s.Status == ClassificationStatus.Processing && s.NextAttemptAt <= now) // Захват зависших
                    )
                    .OrderBy(s => s.LotId)
                    .Take(batchSize)
                    .Select(s => s.LotId)
                    .ToListAsync(stoppingToken);

                if (lotIdsToProcess.Count == 0)
                {
                    _logger.LogInformation("Нет лотов на классификацию. Ждем 20 минут.");
                    await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
                    continue;
                }

                // Батчевая классификация выполняется только если набралось достаточно лотов
                if (lotIdsToProcess.Count < batchSize)
                {
                    _logger.LogInformation("Найдено {Count} лотов, но требуется минимум {BatchSize} для батчевой классификации. Ждем 20 минут.",
                        lotIdsToProcess.Count, batchSize);
                    await Task.Delay(TimeSpan.FromMinutes(20), stoppingToken);
                    continue;
                }

                _logger.LogInformation("Найдено {Count} лотов для восстановления классификации.", lotIdsToProcess.Count);

                // Атомарное бронирование задач (защита от гонок данных между подами/потоками)
                await dbContext.LotClassificationStates
                    .Where(s => lotIdsToProcess.Contains(s.LotId))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(state => state.Status, ClassificationStatus.Processing)
                        .SetProperty(state => state.Attempts, state => state.Attempts + 1)
                        .SetProperty(state => state.NextAttemptAt, now.AddHours(1)),
                        stoppingToken);

                _logger.LogInformation("Забронировано {Count} лотов. Передаем в классификатор.", lotIdsToProcess.Count);

                // Запуск батчевой классификации
                await classificationManager.ClassifyLotsBatchAsync(lotIdsToProcess, "Recovery");

                _logger.LogInformation("Батчевая классификация для {Count} лотов завершена.", lotIdsToProcess.Count);

                // Отправляем url лотов в IndexNow
                if (indexNowService != null)
                {
                    await SubmitToIndexNowAsync(dbContext, indexNowService, lotIdsToProcess, stoppingToken);
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
