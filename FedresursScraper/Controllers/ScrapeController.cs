using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services.Models;
using FedresursScraper.Services;
using Lots.Data;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapeController : ControllerBase
    {
        private readonly IBiddingScraper _biddingScraper;
        private readonly ILotsScraperFromBankruptMessagePage _lotsScraper;
        private readonly ILotsScraperFromLotsPage _lotsScraperFromLotsPage;
        private readonly IWebDriverFactory _driverFactory;
        private readonly ITradeCardLotsStatusScraper _tradeCardLotsStatusScraper;
        private readonly LotsDbContext _dbContext;
        private readonly ILogger<ScrapeController> _logger;
        private readonly ICdtTradeStatusScraper _cdtTradeStatusScraper;
        private readonly RadParserService _radParserService;
        private readonly FedresursTradeResultsParserService _tradeResultsParser;

        public ScrapeController(
            IBiddingScraper biddingScraper,
            ILotsScraperFromBankruptMessagePage lotsScraper,
            ILotsScraperFromLotsPage lotsScraperFromLotsPage,
            IWebDriverFactory driverFactory,
            ITradeCardLotsStatusScraper tradeCardLotsStatusScraper,
            LotsDbContext dbContext,
            ILogger<ScrapeController> logger,
            ICdtTradeStatusScraper cdtTradeStatusScraper,
            RadParserService radParserService,
            FedresursTradeResultsParserService tradeResultsParser)
        {
            _biddingScraper = biddingScraper;
            _lotsScraper = lotsScraper;
            _lotsScraperFromLotsPage = lotsScraperFromLotsPage;
            _driverFactory = driverFactory;
            _tradeCardLotsStatusScraper = tradeCardLotsStatusScraper;
            _dbContext = dbContext;
            _logger = logger;
            _cdtTradeStatusScraper = cdtTradeStatusScraper;
            _radParserService = radParserService;
            _tradeResultsParser = tradeResultsParser;
        }

        [HttpGet("{biddingId}")]
        [ProducesResponseType(typeof(BiddingInfo), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> ScrapeAsync(Guid biddingId)
        {
            if (string.IsNullOrWhiteSpace(biddingId.ToString()))
            {
                return BadRequest("ID cannot be empty.");
            }

            var url = $"https://fedresurs.ru/biddings/{biddingId}";
            _logger.LogInformation("Получен API-запрос на скрапинг торгов: {url}", url);

            using var driver = _driverFactory.CreateDriver();

            try
            {
                var biddingInfo = await _biddingScraper.ScrapeBiddingInfoAsync(driver, biddingId);

                return Ok(biddingInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке API-запроса для торгов {biddingId}", biddingId);
                return StatusCode(500, "An internal server error occurred while scraping the lot.");
            }
        }

        [HttpGet("{biddingId}/lots")]
        [ProducesResponseType(typeof(List<LotInfo>), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> ScrapeLotsAsync(Guid biddingId)
        {
            if (string.IsNullOrWhiteSpace(biddingId.ToString()))
            {
                return BadRequest("Идентификатор торгов не может быть пустым иил null");
            }

            var url = $"https://fedresurs.ru/biddings/{biddingId}/lots";
            _logger.LogInformation("Получен API-запрос на скрапинг лотов: {url}", url);

            using var driver = _driverFactory.CreateDriver();

            try
            {
                var lots = await _lotsScraperFromLotsPage.ScrapeLotsAsync(driver, biddingId);

                return Ok(lots);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке API-запроса на скрапинг лотов: {biddingId}", biddingId);
                return StatusCode(500, "An internal server error occurred while scraping the lot.");
            }
        }

        [HttpGet("bankruptmessages/{bankruptMessageId:guid}")]
        [ProducesResponseType(typeof(List<LotInfo>), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> ScrapeLotsFromBankruptMessagePageAsync(Guid bankruptMessageId)
        {
            if (string.IsNullOrWhiteSpace(bankruptMessageId.ToString()))
            {
                return BadRequest("Идентификатор сообщения о проведения торгов не может быть пустым иил null");
            }

            var url = $"https://fedresurs.ru/bankruptmessages/{bankruptMessageId}";
            _logger.LogInformation("Получен API-запрос на скрапинг лотов: {url}", url);

            using var driver = _driverFactory.CreateDriver();

            try
            {
                var lotsInfo = await _lotsScraper.ScrapeLotsAsync(driver, bankruptMessageId);

                return Ok(lotsInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке API-запроса для bankruptMessage {bankruptMessageId}", bankruptMessageId);
                return StatusCode(500, "An internal server error occurred while scraping the lot.");
            }
        }

        /// <summary>
        /// Парсит статусы торгов/итоговую цену/победителя по всем лотам торгов из TradeCard (old.bankrot.fedresurs.ru)
        /// и возвращает результат в ответе (без сохранения в БД).
        /// </summary>
        [HttpGet("{biddingId}/trade-card-statuses")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetLotsTradeCardStatusesAsync(Guid biddingId, CancellationToken token)
        {
            if (biddingId == Guid.Empty)
            {
                return BadRequest("ID cannot be empty.");
            }

            try
            {
                var lotNumbers = await _dbContext.Lots
                    .AsNoTracking()
                    .Where(l => l.BiddingId == biddingId && l.LotNumber != null && l.LotNumber != "")
                    .Select(l => l.LotNumber!)
                    .Distinct()
                    .ToListAsync(token);

                if (lotNumbers.Count == 0)
                {
                    // Если торгов в БД нет — всё равно можно дергать TradeCard, но без номеров лотов мы
                    // не сможем корректно сопоставить результаты. Пусть клиент сперва загрузит лоты.
                    return NotFound(new { message = "Лоты для этих торгов не найдены в БД (нет LotNumber)." });
                }

                var statuses = await _tradeCardLotsStatusScraper.ScrapeLotsStatusesAsync(biddingId, lotNumbers, token);

                var found = statuses.Values
                    .OrderBy(x => x.LotNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var missing = lotNumbers
                    .Select(n => System.Text.RegularExpressions.Regex.Replace(n.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim())
                    .Where(n => !statuses.ContainsKey(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Ok(new
                {
                    biddingId,
                    lotsRequested = lotNumbers.Count,
                    lotsParsed = found.Count,
                    missingLotNumbers = missing,
                    results = found
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге статусов TradeCard для торгов {biddingId}", biddingId);
                return StatusCode(500, "An internal server error occurred while scraping trade statuses.");
            }
        }

        /// <summary>
        /// Тестовый метод для парсинга общего статуса торгов с площадки ЦДТ.
        /// </summary>
        [HttpGet("{tradeNumber}/cdt-status")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetCdtTradeStatusAsync(string tradeNumber, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(tradeNumber))
            {
                return BadRequest("Номер торгов не может быть пустым.");
            }

            try
            {
                _logger.LogInformation("Запрос API на получение статуса ЦДТ для торгов {TradeNumber}", tradeNumber);

                var status = await _cdtTradeStatusScraper.GetTradeStatusAsync(tradeNumber, token);

                if (string.IsNullOrEmpty(status))
                {
                    return NotFound(new { message = $"Статус для торгов ЦДТ {tradeNumber} не найден или страница недоступна." });
                }

                return Ok(new
                {
                    tradeNumber,
                    platform = "Центр дистанционных торгов",
                    parsedStatus = status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге статуса ЦДТ для торгов {TradeNumber}", tradeNumber);
                return StatusCode(500, "An internal server error occurred while scraping CDT trade status.");
            }
        }

        /// <summary>
        /// Парсинг списка ссылок с конкретной страницы каталога РАД
        /// </summary>
        /// <param name="page">Номер страницы (по умолчанию 1)</param>
        [HttpGet("rad/catalog-urls")]
        public async Task<IActionResult> ScrapeRadCatalogUrls([FromQuery] int page = 1)
        {
            try
            {
                _logger.LogInformation("Начинаем парсинг ссылок РАД со страницы {Page}", page);

                // Базовый URL с добавлением параметра page
                string targetUrl = $"index.php?dispatch=categories.view&category_id=9876&features_hash=172-186359_174-31371-31357&filter_fields[is_archive]=false&page={page}";

                var urls = await _radParserService.GetLotUrlsFromCatalogAsync(targetUrl);

                _logger.LogInformation("Успешно спарсили {Count} ссылок", urls.Count);

                return Ok(new
                {
                    Page = page,
                    TotalFound = urls.Count,
                    Urls = urls
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге каталога РАД");
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        [HttpPost("biddings/{biddingId:guid}/results")]
        public async Task<IActionResult> ScrapeBiddingResults(Guid biddingId, CancellationToken cancellationToken)
        {
            // Запускаем парсер
            var isFound = await _tradeResultsParser.ProcessSingleBiddingAsync(biddingId, cancellationToken);

            if (!isFound)
            {
                return NotFound(new { Message = $"Торги с ID {biddingId} не найдены в базе." });
            }

            // Загружаем торги, чтобы узнать общее количество лотов
            var bidding = await _dbContext.Biddings
                .Include(b => b.Lots)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == biddingId, cancellationToken);

            if (bidding == null)
            {
                return NotFound(new { Message = $"Торги с ID {biddingId} были удалены или не существуют." });
            }

            // Считаем статистику на основе собранных фактов (LotTradeResults)

            // Считаем только лоты с валидными номерами
            var validLotsCount = bidding.Lots.Count(l => !string.IsNullOrWhiteSpace(l.LotNumber));

            // Считаем уникальные номера лотов, для которых парсер нашел хотя бы одно сообщение с результатами
            var lotsWithParsedResults = await _dbContext.LotTradeResults
                .Where(r => r.BiddingId == biddingId)
                .Select(r => r.LotNumber)
                .Distinct()
                .CountAsync(cancellationToken);

            var pendingLots = validLotsCount - lotsWithParsedResults;

            return Ok(new
            {
                Message = $"Парсинг результатов для торгов {biddingId} успешно завершен.",
                Stats = new
                {
                    TotalLots = validLotsCount,
                    LotsWithParsedResults = lotsWithParsedResults,
                    PendingLots = Math.Max(0, pendingLots) // Защита от отрицательных чисел, если база рассинхронизирована
                }
            });
        }
    }
}
