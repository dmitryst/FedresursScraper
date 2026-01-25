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
        private readonly ILogger<EnrichmentController> _logger;

        public EnrichmentController(
            LotsDbContext context,
            ICdtEnrichmentService cdtService,
            IMetsEnrichmentService metsService,
            ILogger<EnrichmentController> logger)
        {
            _context = context;
            _cdtService = cdtService;
            _metsService = metsService;
            _logger = logger;
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
    }
}
