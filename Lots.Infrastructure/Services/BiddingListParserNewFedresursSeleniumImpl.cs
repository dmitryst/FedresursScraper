// FedresursScraper.Services/BiddingListParserNewFedresursSeleniumImpl.cs

using FedresursScraper.Services.Models;
using Microsoft.EntityFrameworkCore;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace FedresursScraper.Services;

/// <summary>
/// Парсинг ленты https://fedresurs.ru/biddings.
/// Early-stop как у каталога Альфалот: не останавливаемся на первом известном ID,
/// а после N порций подряд без новых торгов (с периодическим full-rescan).
/// </summary>
public class BiddingListParserNewFedresursSeleniumImpl : BackgroundService
{
    private readonly ILogger<BiddingListParserNewFedresursSeleniumImpl> _logger;
    private readonly IBiddingDataCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly IConfiguration _configuration;

    private const string BaseUrl = "https://fedresurs.ru/biddings";
    private readonly Random _random = new();

    /// <summary>
    /// Таймер полного обхода. После рестарта null → первый цикл инкрементальный.
    /// </summary>
    private static DateTime? _lastFullRescanUtc;

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
        int intervalMinutes = _configuration.GetValue("Parsing:ListIntervalMinutes", 60);

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

        var stopAfterBatchesWithoutNew = Math.Max(1,
            _configuration.GetValue("Parsing:ListStopAfterBatchesWithoutNew", 2));
        var maxLoadMoreClicks = Math.Max(1,
            _configuration.GetValue("Parsing:ListMaxLoadMoreClicks", 40));
        var fullRescanHours = Math.Max(1,
            _configuration.GetValue("Parsing:ListFullRescanIntervalHours", 24));

        if (!_lastFullRescanUtc.HasValue)
            _lastFullRescanUtc = DateTime.UtcNow;

        var forceFullRescan =
            DateTime.UtcNow - _lastFullRescanUtc.Value >= TimeSpan.FromHours(fullRescanHours);

        _logger.LogInformation(
            "Инициализация ChromeDriver. Mode={Mode}, StopAfterBatchesWithoutNew={StopAfter}, MaxLoadMore={MaxLoadMore}",
            forceFullRescan ? "full" : "incremental",
            stopAfterBatchesWithoutNew,
            maxLoadMoreClicks);

        using var driver = _webDriverFactory.CreateDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));

        var totalEnqueued = 0;
        var consecutiveBatchesWithoutNew = 0;
        var loadMoreClicks = 0;

        try
        {
            _logger.LogInformation("Открытие страницы: {Url}", BaseUrl);
            driver.Navigate().GoToUrl(BaseUrl);

            await Task.Delay(_random.Next(3000, 5000), stoppingToken);

            wait.Until(d => d.FindElements(By.CssSelector("bidding-search-tab-card")).Count > 0);

            var lastProcessedCount = 0;
            var processedIdsThisSession = new HashSet<Guid>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var cards = driver.FindElements(By.CssSelector("bidding-search-tab-card"));
                var batchCards = new List<(Guid Id, string TradeNumber, string Platform)>();

                for (var i = lastProcessedCount; i < cards.Count; i++)
                {
                    var card = cards[i];

                    try
                    {
                        var linkElement = card.FindElement(By.CssSelector(".number-wrapper .number a.underlined"));
                        var tradeNumber = linkElement.Text.Trim();
                        var href = linkElement.GetAttribute("href");

                        var idString = href?.Split('/').LastOrDefault();
                        if (string.IsNullOrWhiteSpace(idString) || !Guid.TryParse(idString, out var biddingId))
                        {
                            _logger.LogWarning("Не удалось извлечь корректный ID из ссылки: {Href}", href);
                            continue;
                        }

                        if (!processedIdsThisSession.Add(biddingId))
                            continue;

                        var platformElement = card.FindElement(By.CssSelector(".tradeplace-name a.underlined"));
                        var platform = platformElement.Text.Trim();

                        batchCards.Add((biddingId, tradeNumber, platform));
                    }
                    catch (NoSuchElementException ex)
                    {
                        _logger.LogWarning(ex, "Не удалось найти необходимый элемент внутри карточки под индексом {Index}.", i);
                    }
                }

                var batchNew = 0;
                var batchKnown = 0;

                if (batchCards.Count > 0)
                {
                    var batchIds = batchCards.Select(c => c.Id).ToList();
                    var existingIds = (await dbContext.Biddings
                            .AsNoTracking()
                            .Where(b => batchIds.Contains(b.Id))
                            .Select(b => b.Id)
                            .ToListAsync(stoppingToken))
                        .ToHashSet();

                    var newBiddings = new List<BiddingData>();
                    foreach (var (id, tradeNumber, platform) in batchCards)
                    {
                        if (existingIds.Contains(id))
                        {
                            batchKnown++;
                            continue;
                        }

                        batchNew++;
                        newBiddings.Add(new BiddingData
                        {
                            Id = id,
                            TradeNumber = tradeNumber,
                            Platform = platform
                        });
                    }

                    if (newBiddings.Count > 0)
                    {
                        var added = _cache.AddMany(newBiddings);
                        totalEnqueued += added;
                        _logger.LogInformation(
                            "Порция ленты: новых {New} (в очередь {Added}), уже в БД {Known}, карточек {Total}",
                            batchNew,
                            added,
                            batchKnown,
                            batchCards.Count);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Порция ленты: новых 0, уже в БД {Known}, карточек {Total}",
                            batchKnown,
                            batchCards.Count);
                    }
                }

                lastProcessedCount = cards.Count;

                // Инкрементальный early-stop: порция без новых → как у каталога Альфалот.
                if (!forceFullRescan && batchCards.Count > 0 && batchNew == 0)
                {
                    consecutiveBatchesWithoutNew++;
                    if (consecutiveBatchesWithoutNew >= stopAfterBatchesWithoutNew)
                    {
                        _logger.LogInformation(
                            "Ранний выход: {Count} порций подряд без новых торгов.",
                            consecutiveBatchesWithoutNew);
                        break;
                    }
                }
                else if (batchNew > 0)
                {
                    consecutiveBatchesWithoutNew = 0;
                }

                if (loadMoreClicks >= maxLoadMoreClicks)
                {
                    _logger.LogInformation(
                        "Достигнут лимит LoadMore={Max}. Enqueued={Enqueued}, Mode={Mode}",
                        maxLoadMoreClicks,
                        totalEnqueued,
                        forceFullRescan ? "full" : "incremental");
                    break;
                }

                try
                {
                    var loadMoreBtn = driver.FindElements(By.CssSelector(".more_btn_wrapper .more_btn")).FirstOrDefault();

                    if (loadMoreBtn != null && loadMoreBtn.Displayed)
                    {
                        _logger.LogInformation("Загрузка следующей порции торгов...");

                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});",
                            loadMoreBtn);

                        await Task.Delay(_random.Next(1000, 2500), stoppingToken);

                        _logger.LogInformation("Нажимаем 'Загрузить еще'...");
                        loadMoreBtn.Click();
                        loadMoreClicks++;

                        wait.Until(d => d.FindElements(By.CssSelector("bidding-search-tab-card")).Count > lastProcessedCount);

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
            _logger.LogInformation(
                "Завершение сессии ChromeDriver. Enqueued={Enqueued}, LoadMoreClicks={Clicks}, Mode={Mode}",
                totalEnqueued,
                loadMoreClicks,
                forceFullRescan ? "full" : "incremental");
            try { driver.Quit(); } catch { /* ignore */ }
        }

        if (forceFullRescan)
            _lastFullRescanUtc = DateTime.UtcNow;
    }
}
