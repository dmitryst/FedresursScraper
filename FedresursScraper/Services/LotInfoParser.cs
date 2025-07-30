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
    private readonly ChromeOptions _chromeOptions;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public LotInfoParser(
        ILogger<LotInfoParser> logger,
        ILotIdsCache cache,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _cache = cache;
        _serviceProvider = serviceProvider;

        _chromeOptions = new ChromeOptions();
        _chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
        _chromeOptions.AddExcludedArgument("enable-automation");
        _chromeOptions.AddAdditionalOption("useAutomationExtension", false);
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

            using var driver = new ChromeDriver(_chromeOptions);
            using var scope = _serviceProvider.CreateScope();
            var scraperService = scope.ServiceProvider.GetRequiredService<ScraperService>();
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
            ViewingProcedure = lotInfo.ViewingProcedure
        };

        // Записываем все категории, исключая пустые
        foreach (var cat in lotInfo.Categories)
        {
            if (!string.IsNullOrWhiteSpace(cat))
                lot.Categories.Add(new LotCategory { Name = cat });
        }

        db.Lots.Add(lot);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Произошла ошибка при сохранении данных в БД.");
        }
    }
}
