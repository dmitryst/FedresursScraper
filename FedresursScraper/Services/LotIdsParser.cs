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

    // --- поля для динамического интервала ---
    private TimeSpan _currentInterval;
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IntervalStep = TimeSpan.FromMinutes(2);

    public LotIdsParser(ILogger<LotIdsParser> logger, ILotIdsCache cache, IWebDriverFactory webDriverFactory)
    {
        _logger = logger;
        _cache = cache;
        _webDriverFactory = webDriverFactory;
        _currentInterval = DefaultInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var addedCount = await UpdateLotIdsAsync();

                AdjustInterval(addedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении лотов с сайта.");
            }

            _logger.LogInformation("Следующая проверка через {Interval}", _currentInterval);
            await Task.Delay(_currentInterval, stoppingToken);
        }
    }

    private async Task<int> UpdateLotIdsAsync()
    {
        _logger.LogInformation("Начинаем обновлять список Id лотов с сайта.");

        using var driver = _webDriverFactory.CreateDriver();

        driver.Navigate().GoToUrl("https://old.bankrot.fedresurs.ru/TradeList.aspx");

        //var page = driver.PageSource;

        await Task.Delay(5000); // дождаться загрузки страницы

        var elements = driver.FindElements(By.CssSelector("a[title='Полная информация о торгах']"))
            .Take(5)
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

        _logger.LogInformation("Добавлено {Count} новых уникальных ID в память.", addedCount);

        return addedCount;
    }

    private void AdjustInterval(int addedCount)
    {
        var previousInterval = _currentInterval;

        if (addedCount == 20)
        {
            _currentInterval -= IntervalStep;
        }
        else if (addedCount <= 10)
        {
            _currentInterval += IntervalStep;
        }
        // В остальных случаях (11-19 лотов) можно постепенно возвращаться к интервалу по умолчанию.
        else
        {
            if (_currentInterval > DefaultInterval)
            {
                _currentInterval -= IntervalStep;
            }
            else if (_currentInterval < DefaultInterval)
            {
                _currentInterval += IntervalStep;
            }
        }

        // Ограничиваем интервал минимальным и максимальным значениями
        _currentInterval = TimeSpan.FromSeconds(
            Math.Clamp(_currentInterval.TotalSeconds, MinInterval.TotalSeconds, MaxInterval.TotalSeconds)
        );

        if (previousInterval != _currentInterval)
        {
            _logger.LogInformation("Интервал проверки изменен с {PreviousInterval} на {CurrentInterval}", previousInterval, _currentInterval);
        }
    }
}
