using FedResursScraper;
using Lots.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium.Chrome;

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
            var lotIds = _cache.GetAllLotIds().ToList();
            if (lotIds.Count == 0)
            {
                _logger.LogInformation("Нет новых ID");

                await Task.Delay(_interval, stoppingToken);
                continue;
            }

            using var driver = _webDriverFactory.CreateDriver();
            using var scope = _serviceProvider.CreateScope();
            var scraperService = scope.ServiceProvider.GetRequiredService<IScraperService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

            foreach (var lotId in lotIds)
            {
                if (string.IsNullOrWhiteSpace(lotId)) continue;

                var lotUrl = $"https://fedresurs.ru/biddings/{lotId}";

                try
                {
                    _logger.LogInformation("Парсинг лота: {LotUrl}", lotUrl);

                    var lotInfo = await scraperService.ScrapeLotData(driver, lotUrl);

                    await SaveToDatabase(lotInfo, dbContext, _logger);

                    _cache.Remove(lotId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке и сохранении лота {LotUrl}", lotUrl);
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
