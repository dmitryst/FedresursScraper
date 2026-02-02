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

        public ScrapeController(
            IBiddingScraper biddingScraper,
            ILotsScraperFromBankruptMessagePage lotsScraper,
            ILotsScraperFromLotsPage lotsScraperFromLotsPage,
            IWebDriverFactory driverFactory,
            ITradeCardLotsStatusScraper tradeCardLotsStatusScraper,
            LotsDbContext dbContext,
            ILogger<ScrapeController> logger)
        {
            _biddingScraper = biddingScraper;
            _lotsScraper = lotsScraper;
            _lotsScraperFromLotsPage = lotsScraperFromLotsPage;
            _driverFactory = driverFactory;
            _tradeCardLotsStatusScraper = tradeCardLotsStatusScraper;
            _dbContext = dbContext;
            _logger = logger;
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
    }
}
