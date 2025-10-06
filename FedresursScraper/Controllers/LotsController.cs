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
    private readonly ILotClassifier _lotClassifier;

    public LotsController(
        ILotCopyService lotCopyService,
        LotsDbContext dbContext,
        ILotClassifier lotClassifier)
    {
        _lotCopyService = lotCopyService;
        _dbContext = dbContext;
        _lotClassifier = lotClassifier;
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetLots(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string[]? categories = null,
        [FromQuery] string? biddingType = null,
        [FromQuery] decimal? priceFrom = null,
        [FromQuery] decimal? priceTo = null)
    {
        var spec = new LotsWithDetailsSpecification(page, pageSize, categories, biddingType, priceFrom, priceTo);

        var totalCountSpec = new LotsCountSpecification(categories, biddingType, priceFrom, priceTo);
        var totalCount = await _dbContext.Lots.WithSpecification(totalCountSpec).CountAsync();

        var lots = await _dbContext.Lots.WithSpecification(spec).ToListAsync();

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

        var result = new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize);

        return Ok(result);
    }

    [HttpGet("{lotId:guid}")]
    public async Task<IActionResult> GetLotById(Guid lotId)
    {
        if (lotId == Guid.Empty)
        {
            return BadRequest(new { message = "Некорректный ID лота." });
        }

        var spec = new LotByIdWithDetailsSpecification(lotId);

        var lot = await _dbContext.Lots.WithSpecification(spec).FirstOrDefaultAsync();

        if (lot == null)
        {
            return NotFound(new { message = "Лот не найден." });
        }

        var lotDto = new LotDto
        {
            Id = lot.Id,
            LotNumber = lot.LotNumber,
            StartPrice = lot.StartPrice,
            Step = lot.Step,
            Deposit = lot.Deposit,
            Description = lot.Description,
            ViewingProcedure = lot.ViewingProcedure,
            CreatedAt = lot.CreatedAt,
            Coordinates = (lot.Latitude.HasValue && lot.Longitude.HasValue)
                ? new[] { lot.Latitude.Value, lot.Longitude.Value }
                : null,
            Bidding = new BiddingDto
            {
                Type = lot.Bidding.Type,
                ViewingProcedure = lot.Bidding.ViewingProcedure
            },
            Categories = lot.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList()
        };

        return Ok(lotDto);
    }

    [HttpGet("with-coordinates")]
    public async Task<IActionResult> GetLotsWithCoordinates()
    {
        var spec = new LotsWithCoordinatesSpecification();

        var lotsWithCoords = await _dbContext.Lots
                                             .WithSpecification(spec)
                                             .ToListAsync();

        var lotsForMap = lotsWithCoords.Select(lot => new LotGeoDto
        {
            Id = lot.Id,
            Title = lot.Description,
            StartPrice = lot.StartPrice,
            Latitude = lot.Latitude.Value,
            Longitude = lot.Longitude.Value
        }).ToList();

        return Ok(lotsForMap);
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

    [HttpGet("all-ids")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllIds()
    {
        var ids = await _dbContext.Lots.Select(l => l.Id).ToListAsync();
        return Ok(ids);
    }
    
    /// <summary>
    /// Классифицирует лот по его названию.
    /// </summary>
    /// <param name="request">Тело запроса с названием лота.</param>
    /// <returns>Название категории.</returns>
    [HttpPost("classify")]
    public async Task<IActionResult> ClassifyLot([FromBody] LotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Title))
        {
            return BadRequest("Параметр 'Title' не может быть пустым.");
        }

        string category = await _lotClassifier.ClassifyLotAsync(request.Title);

        if (category == "Ошибка классификации")
        {
            return StatusCode(500, "Не удалось выполнить классификацию.");
        }

        return Ok(new { Category = category });
    }
}
