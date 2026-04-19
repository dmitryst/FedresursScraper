using Ardalis.Specification.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using Lots.Data.Entities;
using Lots.Data.Specifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly LotsDbContext _context;
    private const int MAX_FAVORITES_PER_USER = 200; // Лимит избранных лотов

    public FavoritesController(LotsDbContext context)
    {
        _context = context;
    }

    //// <summary>
    /// Возвращает пагинированный список лотов, добавленных в "Избранное".
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaginatedResult<LotDto>>> GetFavorites(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20
    )
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        // Получаем все ID избранных лотов (их максимум 200, так что это быстро)
        var allFavoriteLotIds = await _context.Favorites
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.LotId)
            .ToListAsync();

        var totalCount = allFavoriteLotIds.Count;

        if (totalCount == 0)
        {
            return Ok(new PaginatedResult<LotDto>([], 0, 1, pageSize));
        }

        // Применяем пагинацию к списку ID
        var pagedLotIds = allFavoriteLotIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Загружаем сами лоты через спецификацию
        var spec = new LotsByIdsSpecification(pagedLotIds);
        var lots = await _context.Lots
            .WithSpecification(spec)
            .ToListAsync();

        // Сортируем загруженные лоты в том же порядке, в котором они лежат в pagedLotIds
        var sortedLots = pagedLotIds
            .Select(id => lots.FirstOrDefault(l => l.Id == id))
            .Where(l => l != null)
            .ToList();

        var lotDtos = lots.Select(l => new LotDto
        {
            Id = l.Id,
            PublicId = l.PublicId,
            LotNumber = l.LotNumber,
            StartPrice = l.StartPrice,
            Step = l.Step,
            Deposit = l.Deposit,
            Title = l.Title ?? l.Description,
            Slug = l.Slug,
            Description = l.Description,
            ViewingProcedure = l.ViewingProcedure,
            CreatedAt = l.CreatedAt,
            Coordinates = (l.Latitude.HasValue && l.Longitude.HasValue)
                ? new[] { l.Latitude.Value, l.Longitude.Value }
                : null,
            PropertyRegionName = l.PropertyRegionName,
            PropertyFullAddress = l.PropertyFullAddress,
            MarketValue = l.MarketValue,
            MarketValueMin = l.MarketValueMin,
            MarketValueMax = l.MarketValueMax,
            PriceConfidence = l.PriceConfidence,
            InvestmentSummary = l.InvestmentSummary,
            Bidding = new BiddingDto
            {
                Type = l.Bidding.Type,
                BidAcceptancePeriod = l.Bidding.BidAcceptancePeriod,
                ViewingProcedure = l.Bidding.ViewingProcedure,
            },
            Categories = l.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),
            PriceSchedules = l.PriceSchedules.Select(pc => new PriceScheduleDto
            {
                StartDate = pc.StartDate,
                EndDate = pc.EndDate,
                Price = pc.Price
            })
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize));
    }

    // GET: api/favorites/ids
    // Возвращает список GUID
    [Obsolete]
    [HttpGet("ids")]
    public async Task<ActionResult<List<Guid>>> GetFavoriteIds()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var ids = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.LotId)
            .ToListAsync();

        return Ok(ids);
    }

    // POST: api/favorites/toggle/{lotId}
    // Принимает Guid
    [HttpPost("toggle/{lotId}")]
    public async Task<IActionResult> ToggleFavorite(Guid lotId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var existing = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.LotId == lotId);

        if (existing != null)
        {
            _context.Favorites.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { isFavorite = false });
        }
        else
        {
            // Проверка лимита перед добавлением
            var currentCount = await _context.Favorites.CountAsync(f => f.UserId == userId);
            if (currentCount >= MAX_FAVORITES_PER_USER)
            {
                return BadRequest(new { message = $"Нельзя добавить больше {MAX_FAVORITES_PER_USER} лотов в избранное." });
            }

            var favorite = new Favorite { UserId = userId.Value, LotId = lotId };
            _context.Favorites.Add(favorite);
            await _context.SaveChangesAsync();
            return Ok(new { isFavorite = true });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");

        if (idClaim != null && Guid.TryParse(idClaim.Value, out Guid userId))
        {
            return userId;
        }
        return null;
    }
}
