using Lots.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;
using FedresursScraper.Services.Models;

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

                foreach (var biddingId in biddingIdsToParse)
                {
                    if (string.IsNullOrWhiteSpace(biddingId)) continue;

                    var url = $"https://fedresurs.ru/biddings/{biddingId}";

                    try
                    {
                        _logger.LogInformation("Парсинг торгов: {url}", url);

                        var biddingInfo = await biddingScraper.ScrapeDataAsync(driver, Guid.Parse(biddingId));

                        // Если нашли ID сообщения, парсим лоты
                        if (biddingInfo.BankruptMessageId.HasValue)
                        {
                            _logger.LogInformation("Найдено сообщение о банкротстве {MessageId}, парсим лоты.", biddingInfo.BankruptMessageId.Value);
                            biddingInfo.Lots = await lotsScraper.ScrapeLotsAsync(driver, biddingInfo.BankruptMessageId.Value);
                            _logger.LogInformation("Найдено {LotCount} лотов.", biddingInfo.Lots.Count);
                        }
                        else
                        {
                            _logger.LogWarning("Не удалось найти ID сообщения о банкротстве для торгов {BiddingId}. Лоты не будут загружены.", biddingId);
                        }

                        await SaveToDatabase(biddingInfo, dbContext, _logger);

                        _cache.MarkAsCompleted(biddingId);
                        _logger.LogInformation("Торги {biddingId} вместе с лотами успешно обработаны и помечены как 'Completed'.", biddingId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при обработке торгов {biddingId}. Статус останется 'New' для повторной попытки.", biddingId);
                    }
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task SaveToDatabase(BiddingInfo biddingInfo, LotsDbContext db, ILogger logger)
        {
            var bidding = new Bidding
            {
                Id = biddingInfo.Id,
                AnnouncedAt = biddingInfo.AnnouncedAt,
                Type = biddingInfo.Type,
                BidAcceptancePeriod = biddingInfo.BidAcceptancePeriod,
                BankruptMessageId = biddingInfo.BankruptMessageId ?? Guid.Empty,
                ViewingProcedure = biddingInfo.ViewingProcedure,
                CreatedAt = DateTime.UtcNow,
                Lots = []
            };

            db.Biddings.Add(bidding);

            foreach (var lotInfo in biddingInfo.Lots)
            {
                var lot = new Lot
                {
                    // Id будет сгенерирован базой данных
                    LotNumber = lotInfo.Number,
                    Description = lotInfo.Description,
                    StartPrice = lotInfo.StartPrice,
                    Step = lotInfo.Step,
                    Deposit = lotInfo.Deposit,
                    Categories = lotInfo.Categories.Select(c => new LotCategory { Name = c }).ToList()
                };

                bidding.Lots.Add(lot);
            }

            await db.SaveChangesAsync();

            logger.LogInformation("Торги {BiddingId} и {LotCount} лотов сохранены в БД.", bidding.Id, bidding.Lots.Count);
        }
    }
}