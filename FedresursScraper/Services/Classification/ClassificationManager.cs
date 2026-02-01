using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data.Entities;
using Lots.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using FedresursScraper.Services.Utils;

namespace FedresursScraper.Services;

/// <summary>
/// Сервис постановки задач на классификацию лотов. 
/// </summary>
/// <remarks>
/// Отвечает за создание записи Enqueued/Start, выполнение классификации и запись результата.
/// </remarks>
public class ClassificationManager : IClassificationManager
{
    private readonly IClassificationQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ClassificationManager> _logger;

    public ClassificationManager(
        IClassificationQueue taskQueue,
        IServiceProvider serviceProvider,
        ILogger<ClassificationManager> logger)
    {
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task EnqueueClassificationAsync(Guid lotId, string description, string source)
    {
        // Сразу пишем в БД, что лот "Запланирован"
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
            var audit = new LotAuditEvent
            {
                LotId = lotId,
                EventType = "Classification",
                Status = "Enqueued",
                Source = source,
                Timestamp = DateTime.UtcNow
            };
            db.LotAuditEvents.Add(audit);
            await db.SaveChangesAsync();
        }

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
                var result = await classifier.ClassifyLotAsync(description, token);

                if (result == null)
                {
                    throw new Exception("ClassifyLotAsync вернул null");
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
                var lot = await dbContext.Lots
                    .Include(l => l.Categories)
                    .Include(l => l.Bidding)
                        .ThenInclude(b => b.Debtor)
                    .FirstOrDefaultAsync(l => l.Id == lotId, token);
                if (lot != null)
                {
                    lot.Title = result.Title;
                    lot.IsSharedOwnership = result.IsSharedOwnership;
                    lot.MarketValueMin = result.MarketValueMin;
                    lot.MarketValueMax = result.MarketValueMax;
                    lot.PriceConfidence = result.PriceConfidence;
                    lot.InvestmentSummary = result.InvestmentSummary;

                    // Определение местонахождения имущества
                    // 1. Если классификатор нашел местонахождение в описании, используем его
                    if (!string.IsNullOrWhiteSpace(result.PropertyRegionCode) || !string.IsNullOrWhiteSpace(result.PropertyFullAddress))
                    {
                        lot.PropertyRegionCode = result.PropertyRegionCode;
                        lot.PropertyFullAddress = result.PropertyFullAddress;
                        
                        // Если классификатор вернул код региона, но не название - заполняем из справочника
                        if (!string.IsNullOrWhiteSpace(result.PropertyRegionCode) && string.IsNullOrWhiteSpace(result.PropertyRegionName))
                        {
                            var regionInfo = RegionCodeHelper.GetRegionByCode(result.PropertyRegionCode);
                            if (regionInfo.HasValue)
                            {
                                lot.PropertyRegionName = regionInfo.Value.RegionName;
                            }
                            else
                            {
                                lot.PropertyRegionName = result.PropertyRegionName; // Оставляем как есть, если не найдено
                            }
                        }
                        else
                        {
                            lot.PropertyRegionName = result.PropertyRegionName;
                        }
                    }
                    else
                    {
                        // 2. По умолчанию - регион регистрации должника (первые две цифры ИНН)
                        var regionInfo = RegionCodeHelper.GetRegionByInn(lot.Bidding?.Debtor?.Inn);
                        if (regionInfo.HasValue)
                        {
                            lot.PropertyRegionCode = regionInfo.Value.RegionCode;
                            lot.PropertyRegionName = regionInfo.Value.RegionName;
                            lot.PropertyFullAddress = null; // Полный адрес не известен
                        }
                    }

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
            catch (CircuitBreakerOpenException)
            {
                // API DeepSeek недоступно из-за сработанного CircuitBreaker
                dbContext.LotAuditEvents.Add(new LotAuditEvent
                {
                    LotId = lotId,
                    EventType = "Classification",
                    Status = "Skipped", // фактически попытки не было, поэтому устанавливаем специальный статус: Skipped
                    Source = source,
                    Timestamp = DateTime.UtcNow,
                    Details = "Circuit Breaker: API limit/balance"
                });
                await dbContext.SaveChangesAsync(token);

                // притормаживаем немного
                // это заставит поток обработки очереди "уснуть" и не брать новые задачи 
                // следующие 5-10 секунд. Этого достаточно, чтобы не спамить базу
                await Task.Delay(TimeSpan.FromSeconds(10), token);
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
