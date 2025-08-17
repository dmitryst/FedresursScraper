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
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public BiddingWithLotsParser(
            ILogger<BiddingWithLotsParser> logger,
            ILotIdsCache cache,
            IServiceProvider serviceProvider,
            IWebDriverFactory webDriverFactory)
        {
            _logger = logger;
            _cache = cache;
            _serviceProvider = serviceProvider;
            _webDriverFactory = webDriverFactory;
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

                await SaveToDatabaseAsync(biddingInfo, dbContext);
                
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
        private async Task SaveToDatabaseAsync(BiddingInfo biddingInfo, LotsDbContext db)
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
                    Categories = lotInfo.Categories.Select(c => new LotCategory { Name = c }).ToList()
                };
                bidding.Lots.Add(lot);
            }
            
            db.Biddings.Add(bidding);
            await db.SaveChangesAsync();
            
            _logger.LogInformation("Торги {BiddingId} и {LotCount} лотов сохранены в БД.", bidding.Id, bidding.Lots.Count);
        }
    }
}