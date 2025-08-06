using Lots.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;

namespace FedresursScraper.Services
{
    public class LotInfoParser : BackgroundService
    {
        private readonly ILogger<LotInfoParser> _logger;
        private readonly ILotIdsCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebDriverFactory _webDriverFactory;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

        public LotInfoParser(
            ILogger<LotInfoParser> logger,
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
                var lotIdsToParse = _cache.GetIdsToParse();

                if (lotIdsToParse.Count == 0)
                {
                    _logger.LogInformation("Нет новых ID для парсинга.");
                    await Task.Delay(_interval, stoppingToken);
                    continue;
                }

                _logger.LogInformation("В очереди на парсинг {Count} лотов.", lotIdsToParse.Count);

                using var driver = _webDriverFactory.CreateDriver();
                using var scope = _serviceProvider.CreateScope();
                var scraperService = scope.ServiceProvider.GetRequiredService<IScraperService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

                foreach (var lotId in lotIdsToParse)
                {
                    if (string.IsNullOrWhiteSpace(lotId)) continue;

                    var lotUrl = $"https://fedresurs.ru/biddings/{lotId}";

                    try
                    {
                        _logger.LogInformation("Парсинг лота: {LotUrl}", lotUrl);

                        var lotInfo = await scraperService.ScrapeLotData(driver, lotUrl);

                        await SaveToDatabase(lotInfo, dbContext, _logger);

                        _cache.MarkAsCompleted(lotId);
                        _logger.LogInformation("Лот {LotId} успешно обработан и помечен как 'Completed'.", lotId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при обработке лота {LotId}. Статус останется 'New' для повторной попытки.", lotId);
                    }
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task SaveToDatabase(LotInfo lotInfo, LotsDbContext db, ILogger logger)
        {
            var lot = new Lot
            {
                BiddingType = lotInfo.BiddingType,
                Url = lotInfo.Url,
                StartPrice = lotInfo.StartPrice,
                Step = lotInfo.Step,
                Deposit = lotInfo.Deposit,
                Description = lotInfo.Description,
                ViewingProcedure = lotInfo.ViewingProcedure,
                BiddingAnnouncementDate = lotInfo.BiddingAnnouncementDate,
                CreatedAt = DateTime.UtcNow
            };

            // Записываем все категории, исключая пустые
            foreach (var cat in lotInfo.Categories)
            {
                if (!string.IsNullOrWhiteSpace(cat))
                    lot.Categories.Add(new LotCategory { Name = cat });
            }

            db.Lots.Add(lot);

            await db.SaveChangesAsync();
        }
    }
}