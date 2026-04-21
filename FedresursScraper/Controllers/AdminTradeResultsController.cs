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

    public AdminTradeResultsController(
        LotsDbContext dbContext,
        TradeResultsImportService importService)
    {
        _dbContext = dbContext;
        _importService = importService;
    }

    // ВЫЗЫВАЕТСЯ НА ЛОКАЛЬНОМ ПАРСЕРЕ
    [HttpGet("export")]
    public async Task<ActionResult<List<ImportLotTradeResultDto>>> ExportUnsyncedResults()
    {
        var results = await _dbContext.LotTradeResults
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

        return Ok(results);
    }

    // ВЫЗЫВАЕТСЯ НА ПРОДЕ
    [HttpPost("import")]
    public async Task<IActionResult> ImportResults([FromBody] List<ImportLotTradeResultDto> results)
    {
        if (results == null || !results.Any())
            return BadRequest("Список пуст");

        await _importService.ImportResultsAsync(results, HttpContext.RequestAborted);

        return Ok(new { Count = results.Count });
    }

    // ВЫЗЫВАЕТСЯ НА ЛОКАЛЬНОМ ПАРСЕРЕ ПОСЛЕ УСПЕШНОГО IMPORT
    [HttpPost("mark-exported")]
    public async Task<IActionResult> MarkAsExported([FromBody] List<ImportLotTradeResultDto> results)
    {
        if (results == null || !results.Any()) return BadRequest();

        // Собираем пары MessageId + LotNumber для точного поиска в локальной БД
        foreach (var dto in results)
        {
            var record = await _dbContext.LotTradeResults
                .FirstOrDefaultAsync(r => r.MessageId == dto.MessageId && r.LotNumber == dto.LotNumber);

            if (record != null)
            {
                record.IsExportedToProd = true;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }
}