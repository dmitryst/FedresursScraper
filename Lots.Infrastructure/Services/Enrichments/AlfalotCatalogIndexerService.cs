using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FedresursScraper.Services.Enrichments;
using OpenQA.Selenium;

namespace FedresursScraper.Services;

public interface IAlfalotCatalogIndexerService
{
    Task<int> IndexCatalogAsync(CancellationToken ct);
}

/// <summary>
/// Индексация каталога Альфалот через Selenium (обход InProtect WAF).
/// </summary>
public class AlfalotCatalogIndexerService : IAlfalotCatalogIndexerService
{
    private readonly LotsDbContext _context;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly ILogger<AlfalotCatalogIndexerService> _logger;
    private readonly IOptionsMonitor<AlfalotEnrichmentOptions> _options;

    public AlfalotCatalogIndexerService(
        LotsDbContext context,
        IWebDriverFactory webDriverFactory,
        ILogger<AlfalotCatalogIndexerService> logger,
        IOptionsMonitor<AlfalotEnrichmentOptions> options)
    {
        _context = context;
        _webDriverFactory = webDriverFactory;
        _logger = logger;
        _options = options;
    }

    public async Task<int> IndexCatalogAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        // Порог «давности»: лоты, у которых прием заявок закончился раньше этой даты, не индексируем.
        var oldestAllowedApplicationsEnd = DateTime.UtcNow.AddDays(-Math.Abs(options.CatalogMaxPastDays));
        var upserted = 0;
        var consecutiveOldPages = 0;
        var pageTimeout = options.GetPageLoadTimeout();
        var wafTimeout = options.GetWafWaitTimeout();

        _logger.LogInformation(
            "Старт индексации каталога Альфалот (Selenium). MaxPages={MaxPages}, MaxPastDays={Days}, OldestAllowedEnd={Oldest:u}",
            options.CatalogMaxPages,
            options.CatalogMaxPastDays,
            oldestAllowedApplicationsEnd);

        using var driver = _webDriverFactory.CreateDriver();
        driver.Manage().Timeouts().PageLoad = pageTimeout;
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);

        try
        {
            _logger.LogInformation("Открытие каталога Альфалот: {Url}", AlfalotHtmlParser.PurchasesAllUrl);
            AlfalotSeleniumNavigator.OpenAndWait(
                driver,
                AlfalotHtmlParser.PurchasesAllUrl,
                "tr.gridRow, tr.gridAltRow",
                wafTimeout);

            var visitedPages = new HashSet<int>();

            for (var pageGuard = 0; pageGuard < options.CatalogMaxPages; pageGuard++)
            {
                ct.ThrowIfCancellationRequested();

                var html = AlfalotSeleniumNavigator.GetPageHtml(driver);
                var currentPage = AlfalotHtmlParser.ExtractCurrentPageNumber(html) ?? (pageGuard + 1);
                if (!visitedPages.Add(currentPage))
                {
                    _logger.LogInformation("Страница {Page} уже обработана — останавливаем обход.", currentPage);
                    break;
                }

                var rows = AlfalotHtmlParser.ParseCatalogRows(html);
                if (rows.Count == 0)
                {
                    _logger.LogWarning("На странице {Page} каталога Альфалот не найдено строк.", currentPage);
                    break;
                }

                var pageUpserts = 0;
                var pageSkippedOld = 0;

                foreach (var row in rows)
                {
                    if (AlfalotHtmlParser.IsFinishedStatus(row.Status))
                        continue;

                    var relevanceDate = row.ApplicationsEndAt ?? row.EventAt;
                    if (relevanceDate.HasValue && relevanceDate.Value < oldestAllowedApplicationsEnd)
                    {
                        pageSkippedOld++;
                        continue;
                    }

                    await UpsertLinkAsync(row, ct);
                    pageUpserts++;
                    upserted++;
                }

                _logger.LogInformation(
                    "Альфалот каталог стр.{Page}: сохранено {Saved}, пропущено устаревших {Old}, всего строк {Total}",
                    currentPage,
                    pageUpserts,
                    pageSkippedOld,
                    rows.Count);

                if (pageUpserts == 0 && pageSkippedOld == rows.Count)
                {
                    consecutiveOldPages++;
                    if (consecutiveOldPages >= 2)
                    {
                        _logger.LogInformation("Две страницы подряд только с устаревшими лотами — останавливаем обход.");
                        break;
                    }
                }
                else
                {
                    consecutiveOldPages = 0;
                }

                await _context.SaveChangesAsync(ct);

                var nextPage = currentPage + 1;
                var delayMs = options.GetActionDelayMs();
                _logger.LogDebug("Пауза перед следующей страницей каталога: {DelayMs} мс", delayMs);
                await Task.Delay(delayMs, ct);

                _logger.LogInformation("Переход на страницу каталога {NextPage}...", nextPage);
                var moved = AlfalotSeleniumNavigator.TryGoToCatalogPage(driver, nextPage, wafTimeout);
                if (!moved)
                {
                    var stayedOn = AlfalotSeleniumNavigator.ReadCurrentCatalogPage(driver);
                    _logger.LogInformation(
                        "Пагинация каталога Альфалот остановилась после стр.{Page} (сейчас={Current}).",
                        currentPage,
                        stayedOn);
                    break;
                }

                var landedOn = AlfalotSeleniumNavigator.ReadCurrentCatalogPage(driver);
                _logger.LogInformation("После пагинации текущая страница каталога: {Page}", landedOn);
            }
        }
        finally
        {
            try { driver.Quit(); } catch { /* ignore */ }
        }

        _logger.LogInformation("Индексация каталога Альфалот завершена. Upsert={Count}", upserted);
        return upserted;
    }

    private async Task UpsertLinkAsync(AlfalotHtmlParser.CatalogRow row, CancellationToken ct)
    {
        var tradeNorm = AlfalotHtmlParser.NormalizeTradeNumber(row.TradeNumber);
        var lotNorm = AlfalotHtmlParser.NormalizeLotNumber(row.LotNumber);

        var existing = await _context.AlfalotLotLinks
            .FirstOrDefaultAsync(
                x => x.TradeNumberNormalized == tradeNorm && x.LotNumberNormalized == lotNorm,
                ct);

        if (existing == null)
        {
            _context.AlfalotLotLinks.Add(new AlfalotLotLink
            {
                Id = Guid.NewGuid(),
                TradeNumber = row.TradeNumber,
                TradeNumberNormalized = tradeNorm,
                LotNumber = row.LotNumber,
                LotNumberNormalized = lotNorm,
                TradeUrl = row.TradeUrl,
                LotUrl = row.LotUrl,
                Status = row.Status,
                ApplicationsEndAt = row.ApplicationsEndAt,
                EventAt = row.EventAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        existing.TradeNumber = row.TradeNumber;
        existing.LotNumber = row.LotNumber;
        existing.TradeUrl = row.TradeUrl;
        existing.LotUrl = row.LotUrl;
        existing.Status = row.Status;
        existing.ApplicationsEndAt = row.ApplicationsEndAt ?? existing.ApplicationsEndAt;
        existing.EventAt = row.EventAt ?? existing.EventAt;
        existing.UpdatedAt = DateTime.UtcNow;
    }
}
