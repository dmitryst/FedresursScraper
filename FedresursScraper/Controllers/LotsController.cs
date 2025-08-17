using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data.Specifications;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using Ardalis.Specification.EntityFrameworkCore;


namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LotsController : ControllerBase
{
    private readonly ILotCopyService _lotCopyService;
    private readonly LotsDbContext _dbContext;

    public LotsController(ILotCopyService lotCopyService, LotsDbContext dbContext)
    {
        _lotCopyService = lotCopyService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetLots([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var spec = new LotsWithDetailsSpecification(pageNumber, pageSize);
        
        var totalCount = await _dbContext.Lots.CountAsync();

        var lots = await SpecificationEvaluator.Default.GetQuery(_dbContext.Lots.AsQueryable(), spec).ToListAsync();

        var lotDtos = lots.Select(l => new LotDto
        {
            Id = l.Id,
            LotNumber = l.LotNumber,
            StartPrice = l.StartPrice,
            Step = l.Step,
            Deposit = l.Deposit,
            Description = l.Description,
            ViewingProcedure = l.ViewingProcedure,
            CreatedAt = l.CreatedAt,
            Bidding = new BiddingDto
            {
                Type = l.Bidding.Type,
                ViewingProcedure = l.Bidding.ViewingProcedure
            },
            Categories = l.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList()
        }).ToList();

        var result = new PaginatedResult<LotDto>(lotDtos, totalCount, pageNumber, pageSize);

        return Ok(result);
    }

    [HttpPost("{lotId:guid}/copy-to-prod")]
    public async Task<IActionResult> CopyToProd(Guid lotId)
    {
        if (lotId == Guid.Empty)
        {
            return BadRequest("Некорректный ID лота.");
        }

        var success = await _lotCopyService.CopyLotToProdAsync(lotId);

        if (success)
        {
            return Ok(new { message = "Лот успешно скопирован." });
        }

        return StatusCode(500, "Произошла ошибка при копировании лота.");
    }
}
