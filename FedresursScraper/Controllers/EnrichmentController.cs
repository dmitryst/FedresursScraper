using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using FedresursScraper.Services;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EnrichmentController : ControllerBase
    {
        private readonly LotsDbContext _context;
        private readonly ICdtEnrichmentService _cdtService;
        private readonly IMetsEnrichmentService _metsService;
        private readonly IAlfalotEnrichmentService _alfalotService;
        private readonly IAlfalotCatalogIndexerService _alfalotCatalogIndexer;
        private readonly ILogger<EnrichmentController> _logger;

        public EnrichmentController(
            LotsDbContext context,
            ICdtEnrichmentService cdtService,
            IMetsEnrichmentService metsService,
            IAlfalotEnrichmentService alfalotService,
            IAlfalotCatalogIndexerService alfalotCatalogIndexer,
            ILogger<EnrichmentController> logger)
        {
            _context = context;
            _cdtService = cdtService;
            _metsService = metsService;
            _alfalotService = alfalotService;
            _alfalotCatalogIndexer = alfalotCatalogIndexer;
            _logger = logger;
        }

        /// <summary>
        /// Ручной запуск индексации каталога Альфалот (purchases-all → AlfalotLotLinks).
        /// </summary>
        [HttpPost("alfalot/catalog")]
        public async Task<IActionResult> IndexAlfalotCatalog(CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Ручной запуск индексации каталога Альфалот");
                var upserted = await _alfalotCatalogIndexer.IndexCatalogAsync(ct);
                return Ok(new { message = "Индексация каталога Альфалот завершена.", upserted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка индексации каталога Альфалот");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("{tradeNumber}")]
        public async Task<IActionResult> EnrichTrade(string tradeNumber, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(tradeNumber))
                return BadRequest("Номер торгов не указан.");

            try
            {
                // Сначала ищем торги в базе, чтобы понять ПЛОЩАДКУ
                var bidding = await _context.Biddings
                    .AsNoTracking() // Нам нужно только поле Platform
                    .FirstOrDefaultAsync(b => b.TradeNumber == tradeNumber, ct);

                if (bidding == null)
                {
                    return NotFound($"Торги с номером {tradeNumber} не найдены в базе данных. Сначала спарсите их с Федресурса.");
                }

                // Определяем сервис на основе названия площадки
                // Логика определения должна совпадать с той, что в воркерах
                if (IsMets(bidding.Platform))
                {
                    _logger.LogInformation("Запуск ручного обогащения МЭТС для {TradeNumber}", tradeNumber);
                    await _metsService.EnrichByTradeNumberAsync(tradeNumber, ct);
                    return Ok(new { message = $"Торги {tradeNumber} (МЭТС) успешно обогащены." });
                }
                else if (IsCdt(bidding.Platform))
                {
                    _logger.LogInformation("Запуск ручного обогащения ЦДТ для {TradeNumber}", tradeNumber);
                    await _cdtService.EnrichByTradeNumberAsync(tradeNumber, ct);
                    return Ok(new { message = $"Торги {tradeNumber} (ЦДТ) успешно обогащены." });
                }
                else if (IsAlfalot(bidding.Platform))
                {
                    _logger.LogInformation("Запуск ручного обогащения Альфалот для {TradeNumber}", tradeNumber);
                    await _alfalotService.EnrichByTradeNumberAsync(tradeNumber, ct);
                    return Ok(new { message = $"Торги {tradeNumber} (Альфалот) успешно обогащены." });
                }
                else
                {
                    return BadRequest($"Платформа '{bidding.Platform}' пока не поддерживается сервисом обогащения.");
                }
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при ручном запуске обогащения {TradeNumber}", tradeNumber);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Хелперы для определения площадки (можно вынести в общий Utils)
        private bool IsMets(string platform)
        {
            return !string.IsNullOrEmpty(platform) && 
                platform.Contains("Межрегиональная Электронная Торговая Система");
        }

        private bool IsCdt(string platform)
        {
            return !string.IsNullOrEmpty(platform) && 
                platform.Contains("Центр дистанционных торгов");
        }

        private bool IsAlfalot(string platform)
        {
            return !string.IsNullOrEmpty(platform) &&
                platform.Contains(AlfalotEnrichmentService.PlatformMarker, StringComparison.OrdinalIgnoreCase);
        }
    }
}
