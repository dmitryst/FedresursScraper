using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data.Entities;
using Lots.Data;
using System.Text.Json;
using System.Text.Encodings.Web;

/// <summary>
/// Сервис постановки задач на классификацию лотов. 
/// </summary>
/// <remarks>
/// Отвечает за создание записи "Start", выполнение классификации и запись результата.
/// </remarks>
public class ClassificationManager : IClassificationManager
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<ClassificationManager> _logger;

    public ClassificationManager(
        IBackgroundTaskQueue taskQueue,
        ILogger<ClassificationManager> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    public async Task EnqueueClassificationAsync(Guid lotId, string description, string source)
    {
        _logger.LogInformation("Постановка в очередь классификации лота {LotId} (Источник: {Source})", lotId, source);

        await _taskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
            var classifier = scope.ServiceProvider.GetRequiredService<ILotClassifier>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ClassificationManager>>();

            // Аудит: Старт
            var startEvent = new LotAuditEvent
            {
                LotId = lotId,
                EventType = "Classification",
                Status = "Start",
                Source = source,
                Timestamp = DateTime.UtcNow
            };
            dbContext.LotAuditEvents.Add(startEvent);
            await dbContext.SaveChangesAsync(token);

            try
            {
                // Выполнение классификации
                var result = await classifier.ClassifyLotAsync(description);

                if (result == null)
                {
                    throw new Exception("Classifier returned null");
                }

                // Сохраняем аналитику
                var jsonOptions = new JsonSerializerOptions
                {
                    // Делает читаемыми русский текст, спецсимволы (№, «, ») и всё остальное
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var analysisEntry = new LotClassificationAnalysis
                {
                    LotId = lotId,
                    SuggestedCategory = result.SuggestedCategory,
                    SelectedCategories = string.Join(", ", result.Categories),
                    RawResponseJson = JsonSerializer.Serialize(result, jsonOptions),
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.LotClassificationAnalysis.Add(analysisEntry);

                // Обновление лота
                var lot = await dbContext.Lots.Include(l => l.Categories).FirstOrDefaultAsync(l => l.Id == lotId, token);
                if (lot != null)
                {
                    lot.Title = result.Title;
                    lot.IsSharedOwnership = result.IsSharedOwnership;

                    // обновление категорий
                    var validCategories = result.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct();
                    foreach (var catName in validCategories)
                    {
                        if (!lot.Categories.Any(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase)))
                        {
                            dbContext.LotCategories.Add(new LotCategory { Name = catName, LotId = lotId });
                        }
                    }

                    // Аудит: Успех
                    dbContext.LotAuditEvents.Add(new LotAuditEvent
                    {
                        LotId = lotId,
                        EventType = "Classification",
                        Status = "Success",
                        Source = source,
                        Timestamp = DateTime.UtcNow
                    });

                    await dbContext.SaveChangesAsync(token);
                    logger.LogInformation("Лот {LotId} успешно классифицирован.", lotId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка классификации лота {LotId}", lotId);

                // Аудит: Ошибка
                dbContext.LotAuditEvents.Add(new LotAuditEvent
                {
                    LotId = lotId,
                    EventType = "Classification",
                    Status = "Failure",
                    Source = source,
                    Timestamp = DateTime.UtcNow,
                    Details = ex.Message
                });
                await dbContext.SaveChangesAsync(token);
            }
        });
    }
}
