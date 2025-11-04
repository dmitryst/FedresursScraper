// FedresursScraper.Services/BiddingListParser.cs

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using FedresursScraper.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Web;

namespace FedresursScraper.Services;

public class BiddingListParser : BackgroundService
{
    private readonly ILogger<BiddingListParser> _logger;
    private readonly IBiddingDataCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HtmlParser _htmlParser;

    private const string BaseUrl = "https://old.bankrot.fedresurs.ru";
    private const string TradeListUrl = $"{BaseUrl}/TradeList.aspx";

    public BiddingListParser(
        ILogger<BiddingListParser> logger,
        IBiddingDataCache cache,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _cache = cache;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _htmlParser = new HtmlParser();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Запуск нового цикла парсинга списка торгов.");
                await ParseAllPages(stoppingToken);

                // После успешного парсинга очищаем старые записи из кэша.
                _logger.LogInformation("Запуск очистки кэша от обработанных записей.");
                _cache.PruneCompleted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла критическая ошибка в процессе парсинга списка торгов.");
            }

            _logger.LogInformation("Парсинг завершен. Следующий запуск через 1 час.");
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task ParseAllPages(CancellationToken stoppingToken)
    {
        var client = _httpClientFactory.CreateClient("FedresursScraper");
        IDocument document;

        // Начальный GET-запрос для получения первой страницы и токенов
        _logger.LogInformation("Загрузка первой страницы: {Url}", TradeListUrl);
        var initialResponse = await client.GetAsync(TradeListUrl, stoppingToken);
        if (!initialResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Не удалось загрузить начальную страницу. Статус: {StatusCode}", initialResponse.StatusCode);
            return;
        }

        var htmlContent = await initialResponse.Content.ReadAsStringAsync(stoppingToken);
        document = await _htmlParser.ParseDocumentAsync(htmlContent, stoppingToken);

        var currentPage = 1;
        var stopParsing = false;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        while (!stopParsing && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Парсинг страницы {PageNumber}", currentPage);

            var newBiddings = new List<BiddingData>();
            var rows = document.QuerySelectorAll("#ctl00_cphBody_gvTradeList tr");

            // Пропускаем первую строку (заголовок) и последнюю (пагинация)
            foreach (var row in rows.Skip(1).Where(r => !r.ClassList.Contains("pager")))
            {
                var cells = row.QuerySelectorAll("td").ToArray();
                if (cells.Length < 8) // Убедимся, что ячеек достаточно
                {
                    _logger.LogWarning("В строке найдено {CellCount} ячеек вместо ожидаемых 8. Пропускаем.", cells.Length);
                    continue;
                }

                var link = cells[5].QuerySelector("a");
                var href = link?.GetAttribute("href");

                if (string.IsNullOrWhiteSpace(href) || !href.Contains("TradeCard.aspx")) continue;

                var query = HttpUtility.ParseQueryString(new Uri(BaseUrl + href).Query);
                if (!Guid.TryParse(query["ID"], out var biddingId)) continue;

                // Проверяем, есть ли уже такие торги в БД
                if (await dbContext.Biddings.AnyAsync(b => b.Id == biddingId, stoppingToken))
                {
                    _logger.LogInformation("Найдены уже обработанные торги (ID: {BiddingId}). Остановка парсинга.", biddingId);
                    stopParsing = true;
                    break;
                }

                newBiddings.Add(new BiddingData
                {
                    Id = biddingId,
                    TradeNumber = cells[0].TextContent.Trim(),
                    Platform = cells[3].TextContent.Trim()
                });
            }

            if (newBiddings.Any())
            {
                _cache.AddMany(newBiddings);
                _logger.LogInformation("Добавлено {Count} новых торгов в очередь.", newBiddings.Count);
            }

            if (stopParsing) break;

            // Готовимся к переходу на следующую страницу
            currentPage++;

            var viewState = document.QuerySelector("#__VIEWSTATE")?.GetAttribute("value");
            var eventValidation = document.QuerySelector("#__EVENTVALIDATION")?.GetAttribute("value");
            var eventTarget = "ctl00$cphBody$gvTradeList";

            if (string.IsNullOrEmpty(viewState) || string.IsNullOrEmpty(eventValidation))
            {
                _logger.LogWarning("Не удалось найти __VIEWSTATE или __EVENTVALIDATION на странице {Page}. Пагинация невозможна.", currentPage - 1);
                break;
            }

            var formData = new Dictionary<string, string>
            {
                { "__EVENTTARGET", eventTarget },
                { "__EVENTARGUMENT", $"Page${currentPage}" },
                { "__VIEWSTATE", viewState },
                { "__EVENTVALIDATION", eventValidation }
            };

            // Выполняем POST-запрос для получения следующей страницы
            _logger.LogInformation("Переход на страницу {PageNumber}...", currentPage);
            var postResponse = await client.PostAsync(TradeListUrl, new FormUrlEncodedContent(formData), stoppingToken);

            if (!postResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Ошибка при переходе на страницу {Page}. Статус: {StatusCode}", currentPage, postResponse.StatusCode);
                break;
            }

            htmlContent = await postResponse.Content.ReadAsStringAsync(stoppingToken);
            document = await _htmlParser.ParseDocumentAsync(htmlContent, stoppingToken);

            // Проверка, есть ли еще страницы (например, проверив наличие ссылки на следующую страницу)
            var nextPageLink = document.QuerySelectorAll(".pager a").Any(a => a.TextContent.Trim() == currentPage.ToString());
            if (!nextPageLink && document.QuerySelector(".pager span")?.TextContent.Trim() != currentPage.ToString())
            {
                _logger.LogInformation("Достигнут конец списка торгов.");
                break;
            }
        }
    }
}
