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

    public FavoritesController(LotsDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Возвращает список лотов, добавленных текущим авторизованным пользователем в "Избранное".
    /// </summary>
    /// <remarks>
    /// Выполняет поиск записей в таблице избранного для текущего пользователя,
    /// а затем загружает полные данные по найденным лотам (включая связи с торгами и категориями)
    /// с использованием спецификации <see cref="LotsByIdsSpecification"/>.
    /// </remarks>
    /// <returns>Коллекция DTO лотов, находящихся в избранном.</returns>
    /// <response code="200">Успешный запрос. Возвращает список лотов (может быть пустым).</response>
    /// <response code="401">Пользователь не авторизован.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<LotDto>>> GetFavorites()
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var lotIds = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.LotId)
            .ToListAsync();

        if (!lotIds.Any())
        {
            return Ok(new List<LotDto>());
        }

        var spec = new LotsByIdsSpecification(lotIds);

        var lots = await _context.Lots
            .WithSpecification(spec)
            .ToListAsync();

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
            PropertyFullAddress = l.PropertyFullAddress,
            MarketValue = l.MarketValue,
            MarketValueMin = l.MarketValueMin,
            MarketValueMax = l.MarketValueMax,
            PriceConfidence = l.PriceConfidence,
            InvestmentSummary = l.InvestmentSummary,
            Bidding = new BiddingDto
            {
                Type = l.Bidding.Type,
                ViewingProcedure = l.Bidding.ViewingProcedure,
            },
            Categories = l.Categories.Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name
            }).ToList(),
        }).ToList();

        return Ok(lotDtos);
    }

    // GET: api/favorites/ids
    // Возвращает список GUID
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
