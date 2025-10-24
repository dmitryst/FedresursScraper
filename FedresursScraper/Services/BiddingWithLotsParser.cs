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
    public class BiddingWithLotsParser : BackgroundService
    {
        private readonly ILogger<BiddingWithLotsParser> _logger;
        private readonly ILotIdsCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebDriverFactory _webDriverFactory;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public BiddingWithLotsParser(
            ILogger<BiddingWithLotsParser> logger,
            ILotIdsCache cache,
            IServiceProvider serviceProvider,
            IWebDriverFactory webDriverFactory,
            IBackgroundTaskQueue taskQueue)
        {
            _logger = logger;
            _cache = cache;
            _serviceProvider = serviceProvider;
            _webDriverFactory = webDriverFactory;
            _taskQueue = taskQueue;
        }

        /// <summary>
        /// Основной цикл сервиса. Отвечает за получение пакетов ID и управление ресурсами.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var biddingIdsToParse = _cache.GetIdsToParse();
                if (biddingIdsToParse.Count == 0)
                {
                    _logger.LogInformation("Нет новых ID для парсинга.");
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("В очереди на парсинг {Count} торгов.", biddingIdsToParse.Count);

                using var driver = _webDriverFactory.CreateDriver();
                using var scope = _serviceProvider.CreateScope();

                var biddingScraper = scope.ServiceProvider.GetRequiredService<IBiddingScraper>();
                var lotsScraper = scope.ServiceProvider.GetRequiredService<ILotsScraper>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

                foreach (var biddingIdStr in biddingIdsToParse)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    // Делегируем обработку одного ID отдельному методу
                    await ProcessBiddingAsync(biddingIdStr, driver, biddingScraper, lotsScraper, dbContext, stoppingToken);
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        /// <summary>
        /// Обрабатывает один конкретный торг: парсит, получает лоты и сохраняет в БД.
        /// </summary>
        private async Task ProcessBiddingAsync(string biddingIdStr, IWebDriver driver, IBiddingScraper biddingScraper, ILotsScraper lotsScraper, LotsDbContext dbContext, CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(biddingIdStr) || !Guid.TryParse(biddingIdStr, out var biddingId))
            {
                _logger.LogWarning("Некорректный ID торга в кеше: '{BiddingIdStr}'", biddingIdStr);
                return;
            }

            var url = $"https://fedresurs.ru/biddings/{biddingId}";

            try
            {
                _logger.LogInformation("Парсинг торгов: {url}", url);
                var biddingInfo = await biddingScraper.ScrapeDataAsync(driver, biddingId);

                if (biddingInfo.BankruptMessageId.HasValue)
                {
                    var messageId = biddingInfo.BankruptMessageId.Value;

                    // Проверяем, существуют ли лоты, перед тем как их парсить
                    bool lotsExist = await dbContext.Biddings.AnyAsync(b => b.BankruptMessageId == messageId, stoppingToken);
                    if (lotsExist)
                    {
                        _logger.LogInformation("Лоты для сообщения {MessageId} уже есть в БД. Парсинг лотов пропущен.", messageId);
                    }
                    else
                    {
                        biddingInfo.Lots = await lotsScraper.ScrapeLotsAsync(driver, messageId);
                        _logger.LogInformation("Найдено {LotCount} новых лотов.", biddingInfo.Lots.Count);
                    }
                }
                else
                {
                    _logger.LogWarning("Не удалось найти ID сообщения о банкротстве для торгов {BiddingId}. Лоты не будут загружены.", biddingId);
                }

                await SaveBiddingAndEnqueueClassificationAsync(biddingInfo, dbContext);

                _cache.MarkAsCompleted(biddingIdStr);
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
        private async Task SaveBiddingAndEnqueueClassificationAsync(BiddingInfo biddingInfo, LotsDbContext db)
        {
            // Проверка, чтобы избежать ошибки добавления дубликата
            var existingBidding = await db.Biddings.FindAsync(biddingInfo.Id);
            if (existingBidding != null)
            {
                _logger.LogWarning("Торги с ID {BiddingId} уже существуют в БД. Сохранение пропущено.", biddingInfo.Id);
                return;
            }

            var bidding = new Bidding
            {
                Id = biddingInfo.Id,
                AnnouncedAt = biddingInfo.AnnouncedAt,
                Type = biddingInfo.Type,
                BidAcceptancePeriod = biddingInfo.BidAcceptancePeriod,
                BankruptMessageId = biddingInfo.BankruptMessageId ?? Guid.Empty,
                ViewingProcedure = biddingInfo.ViewingProcedure,
                CreatedAt = DateTime.UtcNow,
                Lots = new List<Lot>()
            };

            foreach (var lotInfo in biddingInfo.Lots)
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
                var scopedLogger = scope.ServiceProvider.GetRequiredService<ILogger<BiddingWithLotsParser>>();
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