// FedresursScraper.Services/BiddingListParserSeleniumImpl.cs

using FedresursScraper.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System.Web;

namespace FedresursScraper.Services;

public class BiddingListParserSeleniumImpl : BackgroundService
{
    private readonly ILogger<BiddingListParserSeleniumImpl> _logger;
    private readonly IBiddingDataCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly IConfiguration _configuration;

    private const string BaseUrl = "https://old.bankrot.fedresurs.ru";
    private const string TradeListUrl = $"{BaseUrl}/TradeList.aspx";

    public BiddingListParserSeleniumImpl(
        ILogger<BiddingListParserSeleniumImpl> logger,
        IBiddingDataCache cache,
        IServiceProvider serviceProvider,
        IWebDriverFactory webDriverFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _cache = cache;
        _serviceProvider = serviceProvider;
        _webDriverFactory = webDriverFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalMinutes = _configuration.GetValue<int>("Parsing:ListIntervalMinutes", 60);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Запуск нового цикла парсинга списка торгов через Selenium.");
                await ParseAllPagesAsync(stoppingToken);

                _logger.LogInformation("Запуск очистки кэша от обработанных записей.");
                _cache.PruneCompleted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла критическая ошибка в процессе парсинга списка торгов.");
            }

            _logger.LogInformation("Парсинг завершен. Следующий запуск через {Minutes} минут.", intervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task ParseAllPagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        // Инициализируем WebDriver на один цикл парсинга
        using var driver = _webDriverFactory.CreateDriver();
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);

        _logger.LogInformation("Загрузка начальной страницы: {Url}", TradeListUrl);
        driver.Navigate().GoToUrl(TradeListUrl);

        var currentPage = 1;
        var stopParsing = false;

        // Ждем первичной загрузки таблицы
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

        while (!stopParsing && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ожидание и парсинг страницы {PageNumber}", currentPage);

            try
            {
                wait.Until(d => d.FindElements(By.Id("ctl00_cphBody_gvTradeList")).Count > 0);
            }
            catch (WebDriverTimeoutException)
            {
                _logger.LogWarning("Таблица торгов не найдена на странице {PageNumber}. Остановка парсинга.", currentPage);
                break;
            }

            var newBiddings = new List<BiddingData>();
            var rows = driver.FindElements(By.CssSelector("#ctl00_cphBody_gvTradeList tr"));

            // Пропускаем заголовок (первая строка) и строку пагинации (обычно содержит класс pager)
            var dataRows = rows.Skip(1).Where(r => !r.GetAttribute("class").Contains("pager")).ToList();

            foreach (var row in dataRows)
            {
                var cells = row.FindElements(By.TagName("td"));
                if (cells.Count < 8)
                {
                    continue;
                }

                // Ищем ссылку в 6-й колонке (индекс 5)
                var linkElements = cells[5].FindElements(By.TagName("a"));
                if (linkElements.Count == 0) continue;

                var href = linkElements[0].GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href) || !href.Contains("TradeCard.aspx")) continue;

                var query = HttpUtility.ParseQueryString(new Uri(href).Query);
                if (!Guid.TryParse(query["ID"], out var biddingId)) continue;

                // Проверка в БД
                if (await dbContext.Biddings.AnyAsync(b => b.Id == biddingId, stoppingToken))
                {
                    _logger.LogInformation("Найдены уже обработанные торги (ID: {BiddingId}). Остановка парсинга.", biddingId);
                    stopParsing = true;
                    break;
                }

                newBiddings.Add(new BiddingData
                {
                    Id = biddingId,
                    TradeNumber = cells[0].Text.Trim(),
                    Platform = cells[3].Text.Trim()
                });
            }

            if (newBiddings.Any())
            {
                _cache.AddMany(newBiddings);
                _logger.LogInformation("Добавлено {Count} новых торгов в очередь со страницы {PageNumber}.", newBiddings.Count, currentPage);
            }

            if (stopParsing) break;

            // --- Логика пагинации ---
            currentPage++;
            
            // Ищем ссылку с текстом следующей страницы в строке пагинации
            var nextPageXPath = $"//tr[contains(@class, 'pager')]//a[normalize-space(text())='{currentPage}']";
            var nextPageLinks = driver.FindElements(By.XPath(nextPageXPath));

            if (nextPageLinks.Count > 0)
            {
                _logger.LogInformation("Переход на страницу {PageNumber}...", currentPage);
                try
                {
                    // Прокручиваем к элементу, чтобы избежать ошибки "Element is not clickable"
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", nextPageLinks[0]);
                    await Task.Delay(500, stoppingToken); // Небольшая пауза после скролла
                    
                    nextPageLinks[0].Click();

                    // Так как Федресурс использует UpdatePanel (Ajax), страница не перезагружается целиком.
                    // Ждем несколько секунд, чтобы DOM успел обновиться новыми данными.
                    await Task.Delay(3000, stoppingToken); 
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при клике на страницу {PageNumber}. Остановка.", currentPage);
                    break;
                }
            }
            else
            {
                _logger.LogInformation("Ссылка на страницу {PageNumber} не найдена. Достигнут конец списка.", currentPage);
                break;
            }
        }
        
        _logger.LogInformation("Цикл парсинга завершен. Браузер будет закрыт.");
    }
}