// FedresursScraper.Services/BiddingListParserNewFedresursSeleniumImpl.cs

using FedresursScraper.Services.Models;
using Microsoft.EntityFrameworkCore;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace FedresursScraper.Services;

public class BiddingListParserNewFedresursSeleniumImpl : BackgroundService
{
    private readonly ILogger<BiddingListParserNewFedresursSeleniumImpl> _logger;
    private readonly IBiddingDataCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly IConfiguration _configuration;

    private const string BaseUrl = "https://fedresurs.ru/biddings";
    private readonly Random _random = new Random();

    public BiddingListParserNewFedresursSeleniumImpl(
        ILogger<BiddingListParserNewFedresursSeleniumImpl> logger,
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
                await ParseBiddingsAsync(stoppingToken);

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

    private async Task ParseBiddingsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        _logger.LogInformation("Инициализация ChromeDriver...");
        using var driver = _webDriverFactory.CreateDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

        try
        {
            _logger.LogInformation("Открытие страницы: {Url}", BaseUrl);
            driver.Navigate().GoToUrl(BaseUrl);

            // Имитируем первичный осмотр страницы пользователем
            await Task.Delay(_random.Next(3000, 5000), stoppingToken);

            // Ожидание первоначальной загрузки карточек торгов
            wait.Until(d => d.FindElements(By.CssSelector("bidding-search-tab-card")).Count > 0);

            bool stopParsing = false;
            int lastProcessedCount = 0;
            var processedIdsThisSession = new HashSet<Guid>();

            while (!stopParsing && !stoppingToken.IsCancellationRequested)
            {
                // Запрашиваем актуальный список карточек на каждой итерации, 
                // чтобы избежать StaleElementReferenceException
                var cards = driver.FindElements(By.CssSelector("bidding-search-tab-card"));
                var newBiddings = new List<BiddingData>();

                // Обрабатываем только новые появившиеся карточки
                for (int i = lastProcessedCount; i < cards.Count; i++)
                {
                    var card = cards[i];

                    try
                    {
                        var linkElement = card.FindElement(By.CssSelector(".number-wrapper .number a.underlined"));
                        var tradeNumber = linkElement.Text.Trim();
                        var href = linkElement.GetAttribute("href");

                        // Вытаскиваем GUID из ссылки вида https://fedresurs.ru/biddings/c43b9ac8-2236-4e4a-bddd-20b283aeea09
                        var idString = href?.Split('/').LastOrDefault();
                        if (string.IsNullOrWhiteSpace(idString) || !Guid.TryParse(idString, out var biddingId))
                        {
                            _logger.LogWarning("Не удалось извлечь корректный ID из ссылки: {Href}", href);
                            continue;
                        }

                        // Защита от дублей в рамках одной сессии (на случай непредвиденных обновлений DOM)
                        if (!processedIdsThisSession.Add(biddingId)) continue;

                        // Проверяем наличие торгов в БД
                        if (await dbContext.Biddings.AnyAsync(b => b.Id == biddingId, stoppingToken))
                        {
                            _logger.LogInformation("Найдены уже обработанные торги (ID: {BiddingId}). Остановка парсинга.", biddingId);
                            stopParsing = true;
                            break;
                        }

                        var platformElement = card.FindElement(By.CssSelector(".tradeplace-name a.underlined"));
                        var platform = platformElement.Text.Trim();

                        newBiddings.Add(new BiddingData
                        {
                            Id = biddingId,
                            TradeNumber = tradeNumber,
                            Platform = platform
                        });
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogWarning(ex, "Не удалось найти необходимый элемент внутри карточки под индексом {Index}.", i);
                    }
                }

                if (newBiddings.Any())
                {
                    _cache.AddMany(newBiddings);
                    _logger.LogInformation("Добавлено {Count} новых торгов в очередь.", newBiddings.Count);
                }

                if (stopParsing) break;

                // Обновляем счетчик обработанных
                lastProcessedCount = cards.Count;

                // Ищем кнопку пагинации
                try
                {
                    var loadMoreBtn = driver.FindElements(By.CssSelector(".more_btn_wrapper .more_btn")).FirstOrDefault();

                    if (loadMoreBtn != null && loadMoreBtn.Displayed)
                    {
                        _logger.LogInformation("Загрузка следующей порции торгов...");

                        // Плавно скроллим к кнопке
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", loadMoreBtn);

                        // Пауза после скролла (человек целится мышкой)
                        await Task.Delay(_random.Next(1000, 2500), stoppingToken);

                        _logger.LogInformation("Нажимаем 'Загрузить еще'...");
                        loadMoreBtn.Click(); // Пробуем обычный клик вместо JS для большей реалистичности

                        // Ожидаем подгрузки данных (визуальная задержка)
                        wait.Until(d => d.FindElements(By.CssSelector("bidding-search-tab-card")).Count > lastProcessedCount);

                        // Небольшой рандомный отдых после подгрузки, чтобы не частить запросами
                        await Task.Delay(_random.Next(1500, 3000), stoppingToken);
                    }
                    else
                    {
                        _logger.LogInformation("Кнопка 'Загрузить еще' не найдена или скрыта. Достигнут конец списка.");
                        break;
                    }
                }
                catch (WebDriverTimeoutException)
                {
                    _logger.LogWarning("Таймаут при ожидании новых элементов после нажатия 'Загрузить еще'. Завершаем цикл.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при попытке загрузить следующую страницу.");
                    break;
                }
            }
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogError("Не удалось дождаться загрузки первой страницы торгов. Проверьте соединение или структуру сайта.");
        }
        finally
        {
            _logger.LogInformation("Завершение сессии ChromeDriver.");
            driver.Quit();
        }
    }
}