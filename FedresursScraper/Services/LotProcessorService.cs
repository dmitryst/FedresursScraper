using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FedresursScraper.Services.Models;
using OpenQA.Selenium;

namespace FedresursScraper.Services
{
    public class LotProcessorService : BackgroundService
    {
        private readonly ILogger<LotProcessorService> _logger;
        private readonly ILotDataCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);
        private readonly ICadastralNumberExtractor _cadastralNumberExtractor;
        private readonly IRosreestrServiceClient _rosreestrServiceClient;

        public LotProcessorService(
            ILogger<LotProcessorService> logger,
            ILotDataCache cache,
            IServiceProvider serviceProvider,
            IBackgroundTaskQueue taskQueue,
            ICadastralNumberExtractor cadastralNumberExtractor,
            IRosreestrServiceClient rosreestrServiceClient)
        {
            _logger = logger;
            _cache = cache;
            _serviceProvider = serviceProvider;
            _taskQueue = taskQueue;
            _cadastralNumberExtractor = cadastralNumberExtractor;
            _rosreestrServiceClient = rosreestrServiceClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var lotsToProcess = _cache.GetDataToParse();
                if (lotsToProcess.Count == 0)
                {
                    _logger.LogInformation("Нет новых торгов для обработки.");
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("В очереди на парсинг {Count} торгов.", lotsToProcess.Count);

                using var scope = _serviceProvider.CreateScope();
                var lotDetailParser = scope.ServiceProvider.GetRequiredService<ILotDetailParser>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

                foreach (var initialLotData in lotsToProcess)
                {
                    var lotId = initialLotData.Id;

                    // ПРОВЕРКА НА ДУБЛИКАТ: Перед началом любой работы, убедимся, что лота еще нет в БД.
                    if (await dbContext.Lots.AnyAsync(l => l.Id == lotId, stoppingToken))
                    {
                        _logger.LogWarning("Лот {LotId} уже существует в БД. Пропускаем обработку.", lotId);
                        _cache.MarkAsCompleted(lotId); // Убираем из очереди, чтобы не проверять снова
                        continue;
                    }

                    // Парсим информацию по лоту
                    var details = await lotDetailParser.ParseDetailsAsync(lotId);

                    var cadastralNumbers = _cadastralNumberExtractor.Extract(details.Description!);
                    var coordinates = await _rosreestrServiceClient.FindFirstCoordinatesAsync(details.CadastralNumbers!);

                    var newLot = new Lot
                    {
                        // Данные из первичного парсинга (из кэша)
                        Id = initialLotData.Id,
                        LotNumber = initialLotData.LotNumber,
                        Platform = initialLotData.Platform,

                        // Данные из детального парсинга
                        Type = details.BiddingType,
                        AnnouncedAt = details.AnnouncedAt,
                        BidAcceptancePeriod = details.BidAcceptancePeriod,
                        ViewingProcedure = details.ViewingProcedure,
                        BankruptMessageId = details.BankruptMessageId,
                        Deposit = details.Deposit,
                        StartPrice = details.StartPrice,
                        Step = details.Step,
                        Description = details.Description,
                        Latitude = coordinates?.Latitude,
                        Longitude = coordinates?.Longitude,
                        
                        // Связанные сущности
                        CadastralNumbers = details.CadastralNumbers?
                            .Select(n => new LotCadastralNumber { CadastralNumber = n }).ToList(),
                        Categories = [], // категории будут потом
                        CreatedAt = DateTime.UtcNow
                    };

                    dbContext.Lots.Add(newLot);
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Лот {LotId} успешно создан и сохранен в БД.", newLot.Id);

                    // Помечаем как обработанное
                    _cache.MarkAsCompleted(lotId);

                    // ставим задачу на классификацию лота и его обновления в БД
                    if (!string.IsNullOrEmpty(newLot.Description))
                    {
                        await EnqueueClassificationTaskAsync(newLot.Id, newLot.Description);
                    }
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        /// <summary>
        /// Обрабатывает один конкретный торг: парсит, получает лоты и сохраняет в БД.
        /// </summary>
        private async Task ProcessBiddingAsync(LotData initialLotData, IWebDriver driver, IBiddingScraper biddingScraper, ILotsScraper lotsScraper, LotsDbContext dbContext, CancellationToken stoppingToken)
        {
            var biddingId = initialLotData.Id; // Получаем Guid напрямую
            var url = $"https://fedresurs.ru/biddings/{biddingId}";

            try
            {
                _logger.LogInformation("Парсинг деталей торгов: {url}", url);
                var detailedInfo = await biddingScraper.ScrapeDataAsync(driver, biddingId);

                if (detailedInfo.BankruptMessageId.HasValue)
                {
                    var messageId = detailedInfo.BankruptMessageId.Value;

                    // Проверяем, существуют ли лоты, перед тем как их парсить
                    bool lotsExist = await dbContext.Biddings.AnyAsync(b => b.BankruptMessageId == messageId, stoppingToken);
                    if (lotsExist)
                    {
                        _logger.LogInformation("Лоты для сообщения {MessageId} уже есть в БД. Парсинг лотов пропущен.", messageId);
                    }
                    else
                    {
                        detailedInfo.Lots = await lotsScraper.ScrapeLotsAsync(driver, messageId);
                        _logger.LogInformation("Найдено {LotCount} новых лотов.", detailedInfo.Lots.Count);
                    }
                }
                else
                {
                    _logger.LogWarning("Не удалось найти ID сообщения о банкротстве для торгов {BiddingId}. Лоты не будут загружены.", biddingId);
                }

                await SaveBiddingAndEnqueueClassificationAsync(initialLotData, detailedInfo, dbContext);

                _cache.MarkAsCompleted(biddingId);
                _logger.LogInformation("Торги {biddingId} успешно обработаны и помечены как 'Completed'.", biddingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке торгов {biddingId}. Статус останется 'New' для повторной попытки.", biddingId);
            }
        }

        /// <summary>
        /// Сохраняет информацию о торгах и связанных лотах в базу данных.
        /// </summary>
        private async Task SaveBiddingAndEnqueueClassificationAsync(LotData initialData, BiddingInfo detailedInfo, LotsDbContext db)
        {
            // Проверка, чтобы избежать ошибки добавления дубликата
            var existingBidding = await db.Biddings.FindAsync(detailedInfo.Id);
            if (existingBidding != null)
            {
                _logger.LogWarning("Торги с ID {BiddingId} уже существуют в БД. Сохранение пропущено.", detailedInfo.Id);
                return;
            }

            var bidding = new Bidding
            {
                Id = detailedInfo.Id,
                TradeNumber = initialData.LotNumber,
                Platform = initialData.Platform,
                AnnouncedAt = detailedInfo.AnnouncedAt,
                Type = detailedInfo.Type,
                BidAcceptancePeriod = detailedInfo.BidAcceptancePeriod,
                BankruptMessageId = detailedInfo.BankruptMessageId ?? Guid.Empty,
                ViewingProcedure = detailedInfo.ViewingProcedure,
                CreatedAt = DateTime.UtcNow,
                Lots = new List<Lot>()
            };

            foreach (var lotInfo in detailedInfo.Lots)
            {
                var lot = new Lot
                {
                    LotNumber = lotInfo.Number,
                    Description = lotInfo.Description,
                    StartPrice = lotInfo.StartPrice,
                    Step = lotInfo.Step,
                    Deposit = lotInfo.Deposit,
                    Categories = [],
                    CadastralNumbers = lotInfo.CadastralNumbers?.Select(n => new LotCadastralNumber { CadastralNumber = n }).ToList(),
                    Latitude = lotInfo.Latitude,
                    Longitude = lotInfo.Longitude
                };
                bidding.Lots.Add(lot);
            }

            db.Biddings.Add(bidding);
            await db.SaveChangesAsync();

            _logger.LogInformation("Торги {BiddingId} и {LotCount} лотов сохранены в БД.", bidding.Id, bidding.Lots.Count);

            // ставим задачу на классификацию лота и его обновления в БД
            foreach (var lot in bidding.Lots)
            {
                if (!string.IsNullOrEmpty(lot.Description))
                {
                    await EnqueueClassificationTaskAsync(lot.Id, lot.Description);
                }
            }
        }

        /// <summary>
        /// Создает и ставит в очередь задачу для фоновой классификации лота.
        /// </summary>
        /// <param name="lotId">ID лота в базе данных.</param>
        /// <param name="lotDescription">Описание лота для классификации.</param>
        private async Task EnqueueClassificationTaskAsync(Guid lotId, string lotDescription)
        {
            _logger.LogInformation("Добавление в очередь фоновой классификации лота с ID: {LotId}", lotId);

            await _taskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
            {
                using var scope = serviceProvider.CreateScope();
                var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<LotProcessorService>>();
                var classifier = scope.ServiceProvider.GetRequiredService<ILotClassifier>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

                try
                {
                    var classificationResult = await classifier.ClassifyLotAsync(lotDescription);

                    if (classificationResult == null)
                    {
                        scopedLogger.LogWarning("LotClassifier вернул null для лота с ID {LotId}.", lotId);
                        return;
                    }

                    var lotToUpdate = await dbContext.Lots
                        .Include(l => l.Categories)
                        .FirstOrDefaultAsync(l => l.Id == lotId, token);

                    if (lotToUpdate == null)
                    {
                        scopedLogger.LogWarning("Лот с ID {LotId} не найден для обновления.", lotId);
                        return;
                    }

                    lotToUpdate.Title = classificationResult.Title;
                    lotToUpdate.IsSharedOwnership = classificationResult.IsSharedOwnership;

                    // Обновляем категории
                    var categoryNames = classificationResult.Categories
                        .Select(c => c.Trim())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .Distinct();

                    if (!categoryNames.Any())
                    {
                        scopedLogger.LogWarning("В результате классификации не найдено валидных категорий для лота с ID {LotId}.", lotId);
                    }
                    else
                    {
                        foreach (var name in categoryNames)
                        {
                            if (!lotToUpdate.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                            {
                                var newCategory = new LotCategory { Name = name, LotId = lotId };
                                dbContext.LotCategories.Add(newCategory);
                            }
                        }
                    }

                    await dbContext.SaveChangesAsync(token);

                    scopedLogger.LogInformation(
                        "Лот ID {LotId} успешно обновлен. Название: '{Title}', Доля: {IsShared}, Категории: {Categories}",
                        lotId, lotToUpdate.Title, lotToUpdate.IsSharedOwnership, string.Join(", ", categoryNames));
                }
                catch (Exception ex)
                {
                    scopedLogger.LogError(ex, "Ошибка классификации и обновления лота ID: {LotId}", lotId);
                }
            });
        }
    }
}