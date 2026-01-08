using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services.Models;
using FedresursScraper.Services;

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
        private readonly ILogger<ScrapeController> _logger;

        public ScrapeController(
            IBiddingScraper biddingScraper,
            ILotsScraperFromBankruptMessagePage lotsScraper,
            ILotsScraperFromLotsPage lotsScraperFromLotsPage,
            IWebDriverFactory driverFactory,
            ILogger<ScrapeController> logger)
        {
            _biddingScraper = biddingScraper;
            _lotsScraper = lotsScraper;
            _lotsScraperFromLotsPage = lotsScraperFromLotsPage;
            _driverFactory = driverFactory;
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
    }
}
