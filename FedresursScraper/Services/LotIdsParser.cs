using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

public class LotIdsParser : BackgroundService
{
    private readonly ILogger<LotIdsParser> _logger;
    private readonly ILotIdsCache _cache;
    private readonly ChromeOptions _chromeOptions;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public LotIdsParser(ILogger<LotIdsParser> logger, ILotIdsCache cache)
    {
        _logger = logger;
        _cache = cache;

        _chromeOptions = new ChromeOptions();

        // --- НАИБОЛЕЕ ПОЛНЫЙ НАБОР АРГУМЕНТОВ ДЛЯ DOCKER ---
        // 1. Используем новый, более стабильный headless-режим
        _chromeOptions.AddArgument("--headless=new");
        // 2. Отключаем песочницу (КРИТИЧЕСКИ ВАЖНО для Docker)
        _chromeOptions.AddArgument("--no-sandbox");
        // 3. Отключаем использование /dev/shm
        _chromeOptions.AddArgument("--disable-dev-shm-usage");
        // 4. Отключаем GPU
        _chromeOptions.AddArgument("--disable-gpu");
        // 5. Явно задаем размер окна, это иногда помогает инициализации
        _chromeOptions.AddArgument("--window-size=1920,1080");
        // 6. Отключаем расширения и инфо-панели
        _chromeOptions.AddArgument("--disable-extensions");
        _chromeOptions.AddArgument("--disable-infobars");
        // 7. Явно задаем порт для отладки, чтобы избежать конфликтов
        _chromeOptions.AddArgument("--remote-debugging-port=9222");

        // --- УНИКАЛЬНЫЙ ПРОФИЛЬ ПОЛЬЗОВАТЕЛЯ ---
        // Создаем уникальный путь на каждый запуск
        var userDataDir = $"/tmp/chrome-profile-{Guid.NewGuid()}";
        _chromeOptions.AddArgument($"--user-data-dir={userDataDir}");

        // --- ВАШИ НАСТРОЙКИ ДЛЯ МАСКИРОВКИ ---
        _chromeOptions.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
        _chromeOptions.AddExcludedArgument("enable-automation");
        _chromeOptions.AddAdditionalOption("useAutomationExtension", false);
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

        _logger.LogInformation("--- Попытка запуска ChromeDriver со следующими аргументами: ---");
        foreach (var arg in _chromeOptions.Arguments)
        {
            _logger.LogInformation("Аргумент: {Argument}", arg);
        }
        _logger.LogInformation("----------------------------------------------------------");

        using var driver = new ChromeDriver(_chromeOptions);

        driver.Navigate().GoToUrl("https://old.bankrot.fedresurs.ru/TradeList.aspx");

        var page = driver.PageSource;

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
