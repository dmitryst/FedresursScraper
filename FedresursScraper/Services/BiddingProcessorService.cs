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
    public class BiddingProcessorService : BackgroundService
    {
        private readonly ILogger<BiddingProcessorService> _logger;
        private readonly IBiddingDataCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebDriverFactory _webDriverFactory;
        private readonly IRosreestrQueue _rosreestrQueue;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public BiddingProcessorService(
            ILogger<BiddingProcessorService> logger,
            IBiddingDataCache cache,
            IServiceProvider serviceProvider,
            IWebDriverFactory webDriverFactory,
            IRosreestrQueue rosreestrQueue)
        {
            _logger = logger;
            _cache = cache;
            _serviceProvider = serviceProvider;
            _webDriverFactory = webDriverFactory;
            _rosreestrQueue = rosreestrQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var biddingsToParse = _cache.GetDataToParse();
                if (biddingsToParse.Count == 0)
                {
                    _logger.LogInformation("Нет новых торгов для обработки.");
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("В очереди на парсинг {Count} торгов.", biddingsToParse.Count);

                using var driver = _webDriverFactory.CreateDriver();
                using var scope = _serviceProvider.CreateScope();

                var biddingScraper = scope.ServiceProvider.GetRequiredService<IBiddingScraper>();
                var lotsScraper = scope.ServiceProvider.GetRequiredService<ILotsScraperFromLotsPage>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                var classificationManager = scope.ServiceProvider.GetRequiredService<IClassificationManager>();

                foreach (var biddingData in biddingsToParse)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await ProcessBiddingAsync(biddingData, driver, biddingScraper, lotsScraper, dbContext,
                        classificationManager, stoppingToken);
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        /// <summary>
        /// Обрабатывает один конкретный торг: парсит, получает лоты и сохраняет в БД.
        /// </summary>
        private async Task ProcessBiddingAsync(BiddingData biddingData, IWebDriver driver, IBiddingScraper biddingScraper,
            ILotsScraperFromLotsPage lotsScraper, LotsDbContext dbContext, IClassificationManager classificationManager,
            CancellationToken stoppingToken)
        {
            var biddingId = biddingData.Id;
            var url = $"https://fedresurs.ru/biddings/{biddingId}";

            try
            {
                _logger.LogInformation("Парсинг страницы торгов: {url}", url);

                var biddingInfo = await biddingScraper.ScrapeBiddingInfoAsync(driver, biddingId);
                var scrappedLots = new List<LotInfo>();

                scrappedLots = await lotsScraper.ScrapeLotsAsync(driver, biddingId);
                _logger.LogInformation("Найдено {LotCount} новых лотов.", scrappedLots.Count);


                await SaveBiddingAndEnqueueClassificationAsync(biddingData, biddingInfo, dbContext, scrappedLots, classificationManager);

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
        private async Task SaveBiddingAndEnqueueClassificationAsync(
            BiddingData initialData, BiddingInfo biddingInfo, LotsDbContext db, List<LotInfo> lots,
            IClassificationManager classificationManager)
        {
            // Проверка, чтобы избежать ошибки добавления дубликата
            var existingBidding = await db.Biddings.FindAsync(biddingInfo.Id);
            if (existingBidding != null)
            {
                _logger.LogWarning("Торги с ID {BiddingId} уже существуют в БД. Сохранение пропущено.", biddingInfo.Id);
                return;
            }

            // Логика обработки должника
            if (biddingInfo.DebtorId.HasValue)
            {
                var debtor = await db.Subjects.FindAsync(biddingInfo.DebtorId.Value);
                if (debtor == null)
                {
                    debtor = new Subject
                    {
                        Id = biddingInfo.DebtorId.Value,
                        Name = biddingInfo.DebtorName ?? "неизвестно",
                        Inn = biddingInfo.DebtorInn,
                        Snils = biddingInfo.IsDebtorCompany ? null : biddingInfo.DebtorSnils,
                        Ogrn = biddingInfo.IsDebtorCompany ? biddingInfo.DebtorOgrn : null,
                        Type = biddingInfo.IsDebtorCompany ? SubjectType.Company : SubjectType.Individual
                    };
                    db.Subjects.Add(debtor);
                }
            }

            // Логика обработки арбитражного управляющего
            if (biddingInfo.ArbitrationManagerId.HasValue)
            {
                // Проверям, вдруг мы создали уже этого челоека выше, когда создавали должника
                var manager = await db.Subjects.FindAsync(biddingInfo.ArbitrationManagerId.Value)
                              ?? db.Subjects.Local.FirstOrDefault(p => p.Id == biddingInfo.ArbitrationManagerId.Value);

                if (manager == null)
                {
                    manager = new Subject
                    {
                        Id = biddingInfo.ArbitrationManagerId.Value,
                        Name = biddingInfo.ArbitrationManagerName ?? "неизвестно",
                        Inn = biddingInfo.ArbitrationManagerInn,
                        Snils = null // у арбитражных управляющих нет СНИЛСа в карточке
                    };
                    db.Subjects.Add(manager);
                }
            }

            // Логика обработки судебного дела
            if (biddingInfo.LegalCaseId.HasValue)
            {
                var legalCase = await db.LegalCases.FindAsync(biddingInfo.LegalCaseId.Value);
                if (legalCase == null)
                {
                    legalCase = new LegalCase
                    {
                        Id = biddingInfo.LegalCaseId.Value,
                        CaseNumber = biddingInfo.LegalCaseNumber ?? "неизвестно"
                    };
                    db.LegalCases.Add(legalCase);
                }
            }

            // сохраняем связанные сущности, чтобы избежать ошибок внешних ключей 
            // (хотя EF обычно автоматически обрабатывает это в рамках одной транзакции)
            await db.SaveChangesAsync();

            var bidding = new Bidding
            {
                Id = biddingInfo.Id,
                TradeNumber = initialData.TradeNumber,
                Platform = initialData.Platform,
                AnnouncedAt = biddingInfo.AnnouncedAt,
                Type = biddingInfo.Type,

                BidAcceptancePeriod = biddingInfo.BidAcceptancePeriod,
                TradePeriod = biddingInfo.TradePeriod,
                ResultsAnnouncementDate = biddingInfo.ResultsAnnouncementDate,

                BankruptMessageId = biddingInfo.BankruptMessageId ?? Guid.Empty,

                Organizer = biddingInfo.Organizer,

                DebtorId = biddingInfo.DebtorId,
                ArbitrationManagerId = biddingInfo.ArbitrationManagerId,
                LegalCaseId = biddingInfo.LegalCaseId,

                ViewingProcedure = biddingInfo.ViewingProcedure,

                CreatedAt = DateTime.UtcNow,
                Lots = []
            };

            foreach (var lotInfo in lots)
            {
                var lot = new Lot
                {
                    LotNumber = lotInfo.Number,
                    Description = lotInfo.Description,
                    StartPrice = lotInfo.StartPrice,
                    Step = lotInfo.Step,
                    Deposit = lotInfo.Deposit,
                    Categories = [],
                    CadastralNumbers = lotInfo.CadastralNumbers?
                        .Select(n => new LotCadastralNumber { CadastralNumber = n })
                        .ToList() ?? [],
                };
                bidding.Lots.Add(lot);
            }

            db.Biddings.Add(bidding);
            await db.SaveChangesAsync();

            _logger.LogInformation("Торги {BiddingId} и {LotCount} лотов сохранены в БД.", bidding.Id, bidding.Lots.Count);

            // ставим задачи на классификацию и обновление координат
            foreach (var lot in bidding.Lots)
            {
                // Задача классификации
                if (!string.IsNullOrEmpty(lot.Description))
                {
                    await classificationManager.EnqueueClassificationAsync(lot.Id, lot.Description, "Scraper");
                }

                // Задача обновления координат
                if (lot.CadastralNumbers != null && lot.CadastralNumbers.Any())
                {
                    var numbers = lot.CadastralNumbers.Select(x => x.CadastralNumber).ToList();
                    await EnqueueCoordinateUpdateTaskAsync(lot.Id, numbers);
                }
            }
        }

        /// <summary>
        /// Добавляет задачу на обновление координат в очередь Росреестра
        /// </summary>
        private async Task EnqueueCoordinateUpdateTaskAsync(Guid lotId, List<string> cadastralNumbers)
        {
            _logger.LogInformation("Добавление в очередь Росреестра для лота ID: {LotId}", lotId);

            await _rosreestrQueue.QueueWorkItemAsync(async (serviceProvider, token) =>
            {
                // Сервисы уже в Scope, созданном воркером
                var scopedLogger = serviceProvider.GetRequiredService<ILogger<BiddingProcessorService>>();
                var rosreestrService = serviceProvider.GetRequiredService<IRosreestrServiceClient>();
                var dbContext = serviceProvider.GetRequiredService<LotsDbContext>();

                try
                {
                    var coords = await rosreestrService.FindFirstCoordinatesAsync(cadastralNumbers);

                    if (coords != null)
                    {
                        var lotToUpdate = await dbContext.Lots.FindAsync(new object[] { lotId }, token);
                        if (lotToUpdate != null)
                        {
                            lotToUpdate.Latitude = coords.Latitude;
                            lotToUpdate.Longitude = coords.Longitude;

                            await dbContext.SaveChangesAsync(token);
                            scopedLogger.LogInformation("Координаты обновлены для лота {LotId}", lotId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    scopedLogger.LogError(ex, "Ошибка при получении координат для лота {LotId}", lotId);
                }
            });
        }
    }
}