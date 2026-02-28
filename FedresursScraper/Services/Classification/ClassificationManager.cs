using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data.Entities;
using Lots.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using FedresursScraper.Services.Utils;
using System.Linq;
using System.Globalization;
using System.Text;

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

    public async Task ClassifyLotsBatchAsync(List<Guid> lotIds, string source)
    {
        if (lotIds == null || lotIds.Count == 0)
        {
            _logger.LogInformation("Список лотов для батчевой классификации пуст.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
        var classifier = scope.ServiceProvider.GetRequiredService<ILotClassifier>();

        // Получаем описания лотов из БД вместе с информацией по КН из Росреестра
        var lots = await dbContext.Lots
            .Where(l => lotIds.Contains(l.Id) && !string.IsNullOrEmpty(l.Description))
            .Select(l => new
            {
                l.Id,
                l.Description,
                l.StartPrice,
                CadastralInfos = l.CadastralInfos.Select(ci => new
                {
                    ci.CadastralNumber,
                    ci.Area,
                    ci.CadastralCost,
                    ci.Category,
                    ci.PermittedUse,
                    ci.Address,
                    ci.Status
                }).ToList()
            })
            .ToListAsync();

        if (lots.Count == 0)
        {
            _logger.LogWarning("Не найдено лотов с описаниями для батчевой классификации.");
            return;
        }

        // Собираем Dictionary<Guid, string> уже с обогащённым текстом
        var lotDescriptions = lots.ToDictionary(
            x => x.Id,
            x =>
            {
                // Маппим анонимки в "временные" CadastralInfo только для BuildPromptText
                var infos = x.CadastralInfos.Select(ci => new CadastralInfo
                {
                    CadastralNumber = ci.CadastralNumber,
                    Area = ci.Area,
                    CadastralCost = ci.CadastralCost,
                    Category = ci.Category,
                    PermittedUse = ci.PermittedUse,
                    Address = ci.Address,
                    Status = ci.Status
                }).ToList();

                return BuildPromptText(x.Description!, x.StartPrice, infos);
            }
        );

        _logger.LogInformation("Начинаем батчевую классификацию {Count} лотов (Источник: {Source})", lotDescriptions.Count, source);

        // Аудит: Старт для всех лотов
        var startEvents = lotDescriptions.Keys.Select(lotId => new LotAuditEvent
        {
            LotId = lotId,
            EventType = "Classification",
            Status = "Start",
            Source = source,
            Timestamp = DateTime.UtcNow
        }).ToList();

        dbContext.LotAuditEvents.AddRange(startEvents);
        await dbContext.SaveChangesAsync();

        try
        {
            // Выполнение батчевой классификации
            var results = await classifier.ClassifyLotsBatchAsync(lotDescriptions, CancellationToken.None);

            if (results == null || results.Count == 0)
            {
                _logger.LogWarning("Батчевая классификация не вернула результатов.");
                // Помечаем все лоты как Failure
                await MarkLotsAsFailedAsync(dbContext, lotDescriptions.Keys.ToList(), source, "Батчевая классификация не вернула результатов");
                return;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // Загружаем все лоты с нужными связями для обновления
            var lotsToUpdate = await dbContext.Lots
                .Include(l => l.Categories)
                .Include(l => l.Bidding)
                    .ThenInclude(b => b.Debtor)
                .Where(l => results.Keys.Contains(l.Id))
                .ToListAsync();

            var successCount = 0;
            var failureCount = 0;

            foreach (var lot in lotsToUpdate)
            {
                if (!results.TryGetValue(lot.Id, out var result) || result == null)
                {
                    _logger.LogWarning("Результат классификации отсутствует для лота {LotId}", lot.Id);
                    failureCount++;
                    await MarkLotAsFailedAsync(dbContext, lot.Id, source, "Результат классификации отсутствует");
                    continue;
                }

                try
                {
                    // Сохраняем аналитику
                    var analysisEntry = new LotClassificationAnalysis
                    {
                        LotId = lot.Id,
                        SuggestedCategory = result.SuggestedCategory,
                        SelectedCategories = string.Join(", ", result.Categories),
                        RawResponseJson = JsonSerializer.Serialize(result, jsonOptions),
                        CreatedAt = DateTime.UtcNow
                    };

                    dbContext.LotClassificationAnalysis.Add(analysisEntry);

                    // Обновление лота
                    lot.Title = result.Title;
                    lot.IsSharedOwnership = result.IsSharedOwnership;
                    lot.MarketValueMin = result.MarketValueMin;
                    lot.MarketValueMax = result.MarketValueMax;
                    lot.PriceConfidence = result.PriceConfidence;
                    lot.InvestmentSummary = result.InvestmentSummary;

                    // Определение местонахождения имущества
                    if (!string.IsNullOrWhiteSpace(result.PropertyRegionCode) || !string.IsNullOrWhiteSpace(result.PropertyFullAddress))
                    {
                        lot.PropertyRegionCode = result.PropertyRegionCode;
                        lot.PropertyFullAddress = result.PropertyFullAddress;

                        if (!string.IsNullOrWhiteSpace(result.PropertyRegionCode) && string.IsNullOrWhiteSpace(result.PropertyRegionName))
                        {
                            var regionInfo = RegionCodeHelper.GetRegionByCode(result.PropertyRegionCode);
                            if (regionInfo.HasValue)
                            {
                                lot.PropertyRegionName = regionInfo.Value.RegionName;
                            }
                            else
                            {
                                lot.PropertyRegionName = result.PropertyRegionName;
                            }
                        }
                        else
                        {
                            lot.PropertyRegionName = result.PropertyRegionName;
                        }
                    }
                    else
                    {
                        var regionInfo = RegionCodeHelper.GetRegionByInn(lot.Bidding?.Debtor?.Inn);
                        if (regionInfo.HasValue)
                        {
                            lot.PropertyRegionCode = regionInfo.Value.RegionCode;
                            lot.PropertyRegionName = regionInfo.Value.RegionName;
                            lot.PropertyFullAddress = null;
                        }
                    }

                    // Обновление категорий
                    var validCategories = result.Categories.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct();
                    foreach (var catName in validCategories)
                    {
                        if (!lot.Categories.Any(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase)))
                        {
                            dbContext.LotCategories.Add(new LotCategory { Name = catName, LotId = lot.Id });
                        }
                    }

                    // Аудит: Успех
                    dbContext.LotAuditEvents.Add(new LotAuditEvent
                    {
                        LotId = lot.Id,
                        EventType = "Classification",
                        Status = "Success",
                        Source = source,
                        Timestamp = DateTime.UtcNow
                    });

                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка обработки результата классификации для лота {LotId}", lot.Id);
                    failureCount++;
                    await MarkLotAsFailedAsync(dbContext, lot.Id, source, ex.Message);
                }
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Батчевая классификация завершена: успешно {SuccessCount}, ошибок {FailureCount}", successCount, failureCount);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning("Circuit Breaker открыт. Помечаем все лоты как Skipped.");
            await MarkLotsAsSkippedAsync(dbContext, lotDescriptions.Keys.ToList(), source, "Circuit Breaker: API limit/balance");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка батчевой классификации");
            await MarkLotsAsFailedAsync(dbContext, lotDescriptions.Keys.ToList(), source, ex.Message);
        }
    }

    private static string BuildPromptText(string description, decimal? startPrice, IReadOnlyCollection<CadastralInfo>? infos)
    {
        var sb = new StringBuilder();
        sb.AppendLine(description?.Trim() ?? string.Empty);

        if (startPrice.HasValue)
        {
            sb.AppendLine($"Начальная цена лота: {startPrice.Value.ToString("N2", CultureInfo.InvariantCulture)} руб.");
        }

        if (infos == null || infos.Count == 0)
            return sb.ToString();

        // Чтобы не раздувать промпт: максимум 3 объекта Росреестра
        var topInfos = infos
            .Where(i => !string.IsNullOrWhiteSpace(i.CadastralNumber))
            .Take(3)
            .ToList();

        if (topInfos.Count == 0)
            return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("Данные Росреестра (если есть — считать фактом, не выдумывать):");

        foreach (var i in topInfos)
        {
            sb.Append("- КН: ").Append(i.CadastralNumber);

            if (i.Area.HasValue)
                sb.Append($", площадь: {i.Area.Value.ToString("0.##", CultureInfo.InvariantCulture)} м²");

            if (i.CadastralCost.HasValue)
                sb.Append($", кадастровая стоимость: {i.CadastralCost.Value.ToString(CultureInfo.InvariantCulture)} руб.");

            if (!string.IsNullOrWhiteSpace(i.Category))
                sb.Append($", категория: {i.Category}");

            if (!string.IsNullOrWhiteSpace(i.PermittedUse))
                sb.Append($", ВРИ: {i.PermittedUse}");

            if (!string.IsNullOrWhiteSpace(i.Address))
                sb.Append($", адрес: {i.Address}");

            if (!string.IsNullOrWhiteSpace(i.Status))
                sb.Append($", статус: {i.Status}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task MarkLotAsFailedAsync(LotsDbContext dbContext, Guid lotId, string source, string details)
    {
        dbContext.LotAuditEvents.Add(new LotAuditEvent
        {
            LotId = lotId,
            EventType = "Classification",
            Status = "Failure",
            Source = source,
            Timestamp = DateTime.UtcNow,
            Details = details
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task MarkLotsAsFailedAsync(LotsDbContext dbContext, List<Guid> lotIds, string source, string details)
    {
        var events = lotIds.Select(lotId => new LotAuditEvent
        {
            LotId = lotId,
            EventType = "Classification",
            Status = "Failure",
            Source = source,
            Timestamp = DateTime.UtcNow,
            Details = details
        }).ToList();

        dbContext.LotAuditEvents.AddRange(events);
        await dbContext.SaveChangesAsync();
    }

    private async Task MarkLotsAsSkippedAsync(LotsDbContext dbContext, List<Guid> lotIds, string source, string details)
    {
        var events = lotIds.Select(lotId => new LotAuditEvent
        {
            LotId = lotId,
            EventType = "Classification",
            Status = "Skipped",
            Source = source,
            Timestamp = DateTime.UtcNow,
            Details = details
        }).ToList();

        dbContext.LotAuditEvents.AddRange(events);
        await dbContext.SaveChangesAsync();
    }
}
