using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

public class LotIdsParser : BackgroundService
{
    private readonly ILogger<LotIdsParser> _logger;
    private readonly ILotIdsCache _cache;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public LotIdsParser(ILogger<LotIdsParser> logger, ILotIdsCache cache, IWebDriverFactory webDriverFactory)
    {
        _logger = logger;
        _cache = cache;
        _webDriverFactory = webDriverFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateLotIdsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении лотов с сайта.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task UpdateLotIdsAsync()
    {
        _logger.LogInformation("Начинаем обновлять список Id лотов с сайта.");

        using var driver = _webDriverFactory.CreateDriver();

        driver.Navigate().GoToUrl("https://old.bankrot.fedresurs.ru/TradeList.aspx");

        //var page = driver.PageSource;

        await Task.Delay(5000); // дождаться загрузки страницы

        var elements = driver.FindElements(By.CssSelector("a[title='Полная информация о торгах']"))
            .Take(20)
            .ToList();

        var newIds = new List<string>();

        foreach (var element in elements)
        {
            var href = element.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            var uri = new Uri(new Uri("https://old.bankrot.fedresurs.ru"), href);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var id = query["ID"];
            if (!string.IsNullOrWhiteSpace(id))
            {
                newIds.Add(id);
            }
        }

        var addedCount = _cache.AddMany(newIds);

        _logger.LogInformation("Добавлено {Count} новых уникальных ID лотов.", addedCount);
    }
}
