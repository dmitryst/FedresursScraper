using System.Text.Json;
using FedresursScraper.Services;
using FedresursScraper.Services.Models;
using Lots.Data.Dto;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/admin/trade-results")]
public class AdminTradeResultsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly TradeResultsImportService _importService;
    private readonly IIndexNowService _indexNowService;

    public AdminTradeResultsController(
        LotsDbContext dbContext,
        TradeResultsImportService importService,
        IIndexNowService indexNowService)
    {
        _dbContext = dbContext;
        _importService = importService;
        _indexNowService = indexNowService;
    }

    // ВЫЗЫВАЕТСЯ НА ЛОКАЛЬНОМ ПАРСЕРЕ
    [HttpGet("export")]
    public async Task<ActionResult<TradeSyncBatchDto>> ExportUnsyncedData()
    {
        var batch = new TradeSyncBatchDto();

        // Собираем результаты торгов
        batch.Results = await _dbContext.LotTradeResults
            .Where(r => !r.IsExportedToProd)
            .Select(r => new ImportLotTradeResultDto
            {
                BiddingId = r.BiddingId,
                MessageId = r.MessageId,
                LotNumber = r.LotNumber,
                EventType = r.EventType,
                EventDate = r.EventDate,
                Reason = r.Reason,
                FinalPrice = r.FinalPrice,
                WinnerName = r.WinnerName,
                WinnerInn = r.WinnerInn,
                Status = r.Status,
                DecisionJustification = r.DecisionJustification
            })
            .ToListAsync();

        // Собираем обновления дат проверки
        batch.ScheduleUpdates = await _dbContext.BiddingScheduleUpdates
            .Where(u => !u.IsExported)
            .Select(u => new BiddingScheduleUpdateDto
            {
                BiddingId = u.BiddingId,
                NextStatusCheckAt = u.NextStatusCheckAt
            })
            .ToListAsync();

        return Ok(batch);
    }

    // ВЫЗЫВАЕТСЯ НА ПРОДЕ
    [HttpPost("import")]
    public async Task<IActionResult> ImportBatch([FromBody] TradeSyncBatchDto batch)
    {
        if (batch == null)
            return BadRequest("Пустой пакет");

        // Импортируем результаты
        if (batch.Results.Any())
        {
            await _importService.ImportResultsAsync(batch.Results, HttpContext.RequestAborted);
        }

        // Обновляем даты в Bidding
        if (batch.ScheduleUpdates.Any())
        {
            foreach (var update in batch.ScheduleUpdates)
            {
                // Используем ExecuteUpdate для производительности (EF Core 7+)
                await _dbContext.Biddings
                    .Where(b => b.Id == update.BiddingId)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.NextStatusCheckAt, update.NextStatusCheckAt));
            }
        }

        return Ok(new
        {
            ResultsCount = batch.Results.Count,
            UpdatesCount = batch.ScheduleUpdates.Count
        });
    }

    // ВЫЗЫВАЕТСЯ НА ЛОКАЛЬНОМ ПАРСЕРЕ ПОСЛЕ УСПЕШНОГО IMPORT
    [HttpPost("mark-exported")]
    public async Task<IActionResult> MarkAsExported([FromBody] TradeSyncBatchDto batch)
    {
        if (batch == null)
            return BadRequest();

        // Помечаем результаты как отправленные
        var messageIds = batch.Results.Select(r => r.MessageId).ToList();
        if (messageIds.Any())
        {
            await _dbContext.LotTradeResults
                .Where(r => messageIds.Contains(r.MessageId))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsExportedToProd, true));
        }

        // Помечаем обновления расписания как отправленные
        var biddingIds = batch.ScheduleUpdates.Select(u => u.BiddingId).ToList();
        if (biddingIds.Any())
        {
            await _dbContext.BiddingScheduleUpdates
                .Where(u => biddingIds.Contains(u.BiddingId))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsExported, true));
        }

        return Ok();
    }

    [HttpPost("force-finalize/{biddingId}")]
    public async Task<IActionResult> ForceFinalize(Guid biddingId)
    {
        var bidding = await _dbContext.Biddings
            .Include(b => b.Lots)
            .FirstOrDefaultAsync(b => b.Id == biddingId);

        if (bidding == null)
        {
            return NotFound(new { Error = $"Торги с Id {biddingId} не найдены." });
        }

        // Выполняем доменную логику
        // Источник указываем как "ManualAdminAction", чтобы в аудите было видно вмешательство.
        var changedLots = bidding.ForceFinalizeMissingResults("ManualAdminAction", out var auditEvents);

        if (auditEvents.Any())
        {
            _dbContext.LotAuditEvents.AddRange(auditEvents);
        }

        // Сохраняем все изменения (статусы лотов, статус торгов и аудит) одной транзакцией
        await _dbContext.SaveChangesAsync();

        // Отправляем уведомления в IndexNow
        if (changedLots.Any())
        {
            var urlsToPing = changedLots
                .Select(l => l.GetOrGenerateLotUrl())
                .Distinct()
                .ToList();

            // Отправляем асинхронно через существующий сервис
            // Не дожидаемся ответа (fire-and-forget), чтобы не тормозить UI админки
            _ = _indexNowService.SubmitUrlsAsync(urlsToPing);
        }

        return Ok(new
        {
            Message = "Торги успешно финализированы.",
            BiddingId = biddingId,
            ProcessedLots = changedLots.Count
        });
    }
}