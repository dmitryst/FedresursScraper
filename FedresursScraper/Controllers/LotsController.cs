using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data.Specifications;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using Ardalis.Specification.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Lots.Data.Entities;
using Ardalis.Specification;


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
        [FromQuery] string? searchQuery = null,
        [FromQuery] string? biddingType = null,
        [FromQuery] decimal? priceFrom = null,
        [FromQuery] decimal? priceTo = null,
        [FromQuery] bool? isSharedOwnership = null)
    {
        var spec = new LotsWithDetailsSpecification(
            page, pageSize, categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership);

        var filterSpec = new LotsFilterSpecification(
            categories, searchQuery, biddingType, priceFrom, priceTo, isSharedOwnership);

        var totalCount = await _dbContext.Lots.WithSpecification(filterSpec).CountAsync();

        var lots = await _dbContext.Lots.WithSpecification(spec).ToListAsync();

        var lotDtos = lots.Select(l => new LotDto
        {
            Id = l.Id,
            PublicId = l.PublicId,
            LotNumber = l.LotNumber,
            StartPrice = l.StartPrice,
            Step = l.Step,
            Deposit = l.Deposit,
            Title = l.Title ?? l.Description,
            Description = l.Description,
            ViewingProcedure = l.ViewingProcedure,
            CreatedAt = l.CreatedAt,
            Coordinates = (l.Latitude.HasValue && l.Longitude.HasValue)
                ? new[] { l.Latitude.Value, l.Longitude.Value }
                : null,
            PropertyRegionName = l.PropertyRegionName,
            MarketValue = l.MarketValue,
            InvestmentSummary = l.InvestmentSummary,
            Bidding = new BiddingDto
            {
                Type = l.Bidding.Type,
                ViewingProcedure = l.Bidding.ViewingProcedure
            },
            Categories = l.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),

            Images = l.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList()
        }).ToList();

        var result = new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize);

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLotAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Некорректный ID лота." });
        }

        ISingleResultSpecification<Lot>? spec = null;

        if (int.TryParse(id, out int publicId))
        {
            spec = new LotByIdWithDetailsSpecification(publicId);
        }
        else if (Guid.TryParse(id, out Guid guidId))
        {
            spec = new LotByIdWithDetailsSpecification(guidId);
        }
        else
        {
            // Если это не число и не GUID (например, "some-slug-123" без правильной обработки на фронте)
            return BadRequest(new { message = "Неверный формат ID." });
        }

        var lot = await _dbContext.Lots.WithSpecification(spec).FirstOrDefaultAsync();

        if (lot == null)
        {
            return NotFound(new { message = "Лот не найден." });
        }

        var lotDto = new LotDto
        {
            Id = lot.Id,
            PublicId = lot.PublicId,
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
            PropertyRegionName = lot.PropertyRegionName,
            PropertyFullAddress = lot.PropertyFullAddress,
            MarketValue = lot.MarketValue,
            InvestmentSummary = lot.InvestmentSummary,

            Bidding = new BiddingDto
            {
                Type = lot.Bidding.Type,
                Platform = lot.Bidding.Platform,
                BidAcceptancePeriod = lot.Bidding.BidAcceptancePeriod,
                TradePeriod = lot.Bidding.TradePeriod,
                ResultsAnnouncementDate = lot.Bidding.ResultsAnnouncementDate,
                ViewingProcedure = lot.Bidding.ViewingProcedure,

            },
            Categories = lot.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),

            PriceSchedules = lot.PriceSchedules
                .OrderBy(ps => ps.StartDate)
                .Select((ps, index) => new PriceScheduleDto
                {
                    Number = index + 1,
                    StartDate = ps.StartDate,
                    EndDate = ps.EndDate,
                    Price = ps.Price,
                    Deposit = ps.Deposit,
                    EstimatedRank = ps.EstimatedRank,
                    PotentialRoi = ps.PotentialRoi
                }).ToList(),

            Images = lot.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList(),

            Documents = lot.Documents
                .Select(d => new LotDocumentDto
                {
                    Id = d.Id,
                    Url = d.Url,
                    Title = d.Title,
                    Extension = d.Extension
                }).ToList(),
        };

        return Ok(lotDto);
    }

    /// <summary>
    /// Возвращает лоты с координатами.
    /// Для анонимных пользователей или пользователей без подписки возвращает не более 100 лотов.
    /// Для пользователей с активной подпиской возвращает все лоты.
    /// </summary>
    [HttpGet("with-coordinates")]
    public async Task<IActionResult> GetLotsWithCoordinates([FromQuery] string[]? categories = null)
    {
        AccessLevel accessLevel = AccessLevel.Anonymous;

        // Проверяем, аутентифицирован ли пользователь
        if (User.Identity?.IsAuthenticated == true)
        {
            // Получаем ID пользователя из токена
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(userIdString, out var userId))
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null)
                {
                    return Unauthorized("Пользователь не найден");
                }

                // Проверяем, активна ли подписка
                if (user.IsSubscriptionActive &&
                    user.SubscriptionEndDate.HasValue &&
                    user.SubscriptionEndDate.Value > DateTime.UtcNow)
                {
                    accessLevel = AccessLevel.Full;
                }
                else
                {
                    accessLevel = AccessLevel.Limited;
                }
            }
        }

        var spec = new LotsWithCoordinatesSpecification(categories);
        var query = _dbContext.Lots.WithSpecification(spec);

        var totalCount = await query.CountAsync();

        List<Lot> lotsToShow;

        if (accessLevel == AccessLevel.Full)
        {
            lotsToShow = await query.ToListAsync();
        }
        else
        {
            // Если нет подписки или пользователь анонимный, берем только первые 100
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
            TotalCount = totalCount,
            AccessLevel = accessLevel
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

    [HttpGet("sitemap-data")]
    public async Task<IActionResult> GetSitemapData(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 40000)
    {
        if (pageSize > 50000)
        {
            pageSize = 50000;
        }

        var query = _dbContext.Lots.AsNoTracking();

        var items = await query
            .Where(l => !string.IsNullOrEmpty(l.Title))
            .OrderByDescending(l => l.CreatedAt) // Свежие сначала
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new SitemapItemDto
            {
                PublicId = l.PublicId,
                Title = l.Title!,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>
    /// Классифицирует лот по его описанию.
    /// </summary>
    /// <param name="request">Тело запроса с описанием лота.</param>
    /// <returns>Результат классификации.</returns>
    [HttpPost("classify")]
    public async Task<IActionResult> ClassifyLot([FromBody] LotRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request?.Description))
        {
            return BadRequest("Параметр 'Description' не может быть пустым.");
        }

        var lotClassificationResult = await _lotClassifier.ClassifyLotAsync(request.Description, token);

        if (lotClassificationResult?.Categories is null)
        {
            return StatusCode(500, "Не удалось выполнить классификацию категорий лота.");
        }

        return Ok(new { result = lotClassificationResult });
    }
}
