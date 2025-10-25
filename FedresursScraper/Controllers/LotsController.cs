using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data.Specifications;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Lots.Data.Entities;


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
            Title = l.Title ?? l.Description,
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
            Title = lot.Title,
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
    [Authorize]
    public async Task<IActionResult> GetLotsWithCoordinates([FromQuery] string[]? categories = null)
    {
        // Получаем ID пользователя из токена
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Unauthorized("Неверный ID пользователя в JWT");
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return Unauthorized("Пользователь не найден");
        }
        bool hasFullAccess = user?.IsSubscriptionActive ?? false;

        var spec = new LotsWithCoordinatesSpecification(categories);
        var query = _dbContext.Lots.WithSpecification(spec);

        var totalCount = await query.CountAsync();

        List<Lot> lotsToShow;

        if (hasFullAccess)
        {
            lotsToShow = await query.ToListAsync();
        }
        else
        {
            // Если нет подписки, берем только первые 100
            lotsToShow = await query.Take(100).ToListAsync();
        }

        var lotsForMap = lotsToShow
            .Select(lot => new LotGeoDto
            {
                Id = lot.Id,
                Title = lot.Title ?? lot.Description,
                StartPrice = lot.StartPrice,
                Latitude = lot.Latitude.GetValueOrDefault(),
                Longitude = lot.Longitude.GetValueOrDefault(),
            }).ToList();

        var response = new MapLotsResponse
        {
            Lots = lotsForMap,
            HasFullAccess = hasFullAccess,
            TotalCount = totalCount
        };

        return Ok(response);
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
    /// Классифицирует лот по его описанию.
    /// </summary>
    /// <param name="request">Тело запроса с описанием лота.</param>
    /// <returns>Результат классификации.</returns>
    [HttpPost("classify")]
    public async Task<IActionResult> ClassifyLot([FromBody] LotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Description))
        {
            return BadRequest("Параметр 'Description' не может быть пустым.");
        }

        var lotClassificationResult = await _lotClassifier.ClassifyLotAsync(request.Description);

        if (lotClassificationResult?.Categories is null)
        {
            return StatusCode(500, "Не удалось выполнить классификацию категорий лота.");
        }

        return Ok(new { result = lotClassificationResult });
    }
}
