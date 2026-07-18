using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using FedresursScraper.Services;
using FedresursScraper.Services.Enrichments;

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
        private readonly IRadEnrichmentService _radService;
        private readonly IRadCatalogIndexerService _radCatalogIndexer;
        private readonly ILogger<EnrichmentController> _logger;

        public EnrichmentController(
            LotsDbContext context,
            ICdtEnrichmentService cdtService,
            IMetsEnrichmentService metsService,
            IAlfalotEnrichmentService alfalotService,
            IAlfalotCatalogIndexerService alfalotCatalogIndexer,
            IRadEnrichmentService radService,
            IRadCatalogIndexerService radCatalogIndexer,
            ILogger<EnrichmentController> logger)
        {
            _context = context;
            _cdtService = cdtService;
            _metsService = metsService;
            _alfalotService = alfalotService;
            _alfalotCatalogIndexer = alfalotCatalogIndexer;
            _radService = radService;
            _radCatalogIndexer = radCatalogIndexer;
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

        /// <summary>
        /// Торги из AlfalotLotLinks, которых нет в Biddings (по нормализованному номеру).
        /// Помогает проверить, не пропускает ли парсер ленты Федресурса.
        /// </summary>
        [HttpGet("alfalot/orphan-links")]
        public async Task<IActionResult> GetAlfalotOrphanLinks(
            [FromQuery] int take = 100,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 1000);

            var linkGroups = await _context.AlfalotLotLinks
                .AsNoTracking()
                .GroupBy(x => x.TradeNumberNormalized)
                .Select(g => new
                {
                    TradeNumberNormalized = g.Key,
                    TradeNumber = g.Min(x => x.TradeNumber)!,
                    LotLinksCount = g.Count(),
                    LastUpdatedAt = g.Max(x => x.UpdatedAt)
                })
                .ToListAsync(ct);

            var biddingTradeNumbers = await _context.Biddings
                .AsNoTracking()
                .Select(b => b.TradeNumber)
                .ToListAsync(ct);

            var existingNorms = biddingTradeNumbers
                .Select(AlfalotHtmlParser.NormalizeTradeNumber)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal);

            var orphans = linkGroups
                .Where(g => !existingNorms.Contains(g.TradeNumberNormalized))
                .OrderByDescending(g => g.LastUpdatedAt)
                .ToList();

            var totalMissing = orphans.Count;
            var sample = orphans.Take(take).ToList();

            return Ok(new
            {
                alfalotDistinctTrades = linkGroups.Count,
                totalMissing,
                sampleTake = take,
                truncated = totalMissing > take,
                items = sample
            });
        }

        /// <summary>
        /// Ручной запуск индексации каталога РАД (имущество должников → RadLotLinks).
        /// </summary>
        [HttpPost("rad/catalog")]
        public async Task<IActionResult> IndexRadCatalog(CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("Ручной запуск индексации каталога РАД");
                var upserted = await _radCatalogIndexer.IndexCatalogAsync(ct);
                return Ok(new { message = "Индексация каталога РАД завершена.", upserted });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка индексации каталога РАД");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Связки RadLotLinks без соответствующих Biddings (по нормализованному ЕФРСБ id).
        /// </summary>
        [HttpGet("rad/orphan-links")]
        public async Task<IActionResult> GetRadOrphanLinks(
            [FromQuery] int take = 100,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 1000);

            var linkGroups = await _context.RadLotLinks
                .AsNoTracking()
                .GroupBy(x => x.EfrsbLotIdNormalized)
                .Select(g => new
                {
                    EfrsbLotIdNormalized = g.Key,
                    EfrsbLotId = g.Min(x => x.EfrsbLotId)!,
                    LotLinksCount = g.Count(),
                    LastUpdatedAt = g.Max(x => x.UpdatedAt)
                })
                .ToListAsync(ct);

            var biddingTradeNumbers = await _context.Biddings
                .AsNoTracking()
                .Select(b => b.TradeNumber)
                .ToListAsync(ct);

            var existingNorms = biddingTradeNumbers
                .Select(RadHtmlParser.NormalizeEfrsbLotId)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal);

            var orphans = linkGroups
                .Where(g => !existingNorms.Contains(g.EfrsbLotIdNormalized))
                .OrderByDescending(g => g.LastUpdatedAt)
                .ToList();

            var totalMissing = orphans.Count;
            var sample = orphans.Take(take).ToList();

            return Ok(new
            {
                radDistinctTrades = linkGroups.Count,
                totalMissing,
                sampleTake = take,
                truncated = totalMissing > take,
                items = sample
            });
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
                else if (IsRad(bidding.Platform))
                {
                    _logger.LogInformation("Запуск ручного обогащения РАД для {TradeNumber}", tradeNumber);
                    await _radService.EnrichByTradeNumberAsync(tradeNumber, ct);
                    return Ok(new { message = $"Торги {tradeNumber} (РАД) успешно обогащены." });
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

        private bool IsRad(string platform)
        {
            return !string.IsNullOrEmpty(platform) &&
                platform.Contains(RadEnrichmentService.PlatformMarker, StringComparison.OrdinalIgnoreCase);
        }
    }
}
