using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/admin/lots")]
[Authorize]
public class AdminLotsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;

    public AdminLotsController(LotsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private async Task<bool> IsAdminAsync()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId)) return false;
        var user = await _dbContext.Users.FindAsync(userId);
        return user?.IsAdmin == true;
    }

    /// <summary>
    /// Лоты, у которых описание не содержит информации об имуществе.
    /// </summary>
    [HttpGet("needs-description")]
    public async Task<IActionResult> GetLotsNeedingDescription(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool activeOnly = true)
    {
        if (!await IsAdminAsync()) return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.Lots
            .AsNoTracking()
            .Where(l => l.NeedsDescriptionReview);

        if (activeOnly)
        {
            query = query.Where(l => l.TradeStatus == null || !Lot.FinalTradeStatuses.Contains(l.TradeStatus));
        }

        var totalCount = await query.CountAsync();

        var lots = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.PublicId,
                l.LotNumber,
                l.Title,
                l.Slug,
                l.Description,
                l.StartPrice,
                l.TradeStatus,
                l.CreatedAt,
                TradeNumber = l.Bidding.TradeNumber,
                Platform = l.Bidding.Platform
            })
            .ToListAsync();

        var items = lots.Select(l =>
        {
            var slug = !string.IsNullOrWhiteSpace(l.Slug)
                ? l.Slug
                : (!string.IsNullOrWhiteSpace(l.Title) ? SlugHelper.GenerateSlug(l.Title) : "lot");

            return new
            {
                l.Id,
                l.PublicId,
                l.LotNumber,
                l.Title,
                l.Slug,
                l.Description,
                l.StartPrice,
                l.TradeStatus,
                l.CreatedAt,
                l.TradeNumber,
                l.Platform,
                Url = $"/lot/{slug}-{l.PublicId}"
            };
        });

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("needs-description/count")]
    public async Task<IActionResult> GetNeedsDescriptionCount([FromQuery] bool activeOnly = true)
    {
        if (!await IsAdminAsync()) return Forbid();

        var query = _dbContext.Lots.Where(l => l.NeedsDescriptionReview);

        if (activeOnly)
        {
            query = query.Where(l => l.TradeStatus == null || !Lot.FinalTradeStatuses.Contains(l.TradeStatus));
        }

        var count = await query.CountAsync();
        return Ok(new { count });
    }
}
