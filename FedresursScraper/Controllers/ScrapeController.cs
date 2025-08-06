using Microsoft.AspNetCore.Mvc;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScrapeController : ControllerBase
    {
        private readonly IScraperService _scraperService;
        private readonly IWebDriverFactory _driverFactory;
        private readonly ILogger<ScrapeController> _logger;

        public ScrapeController(
            IScraperService scraperService, 
            IWebDriverFactory driverFactory, 
            ILogger<ScrapeController> logger)
        {
            _scraperService = scraperService;
            _driverFactory = driverFactory;
            _logger = logger;
        }

        [HttpGet("{lotId}")]
        [ProducesResponseType(typeof(LotInfo), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> ScrapeLot(string lotId)
        {
            if (string.IsNullOrWhiteSpace(lotId))
            {
                return BadRequest("Lot ID cannot be empty.");
            }

            var lotUrl = $"https://fedresurs.ru/biddings/{lotId}";
            _logger.LogInformation("Получен API-запрос на скрапинг лота: {LotUrl}", lotUrl);

            using var driver = _driverFactory.CreateDriver();
            
            try
            {
                var lotInfo = await _scraperService.ScrapeLotData(driver, lotUrl);
                
                if (lotInfo.Description.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
                {
                     return NotFound($"Lot with ID {lotId} could not be scraped or does not exist.");
                }
                
                return Ok(lotInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке API-запроса для лота {LotId}", lotId);
                return StatusCode(500, "An internal server error occurred while scraping the lot.");
            }
        }
    }
}
