using Lots.Data;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FedresursScraper.UserAds.Controllers;

[ApiController]
[Route("api/admin/ads")]
[Authorize]
public class AdminAdsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;

    public AdminAdsController(LotsDbContext dbContext)
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

    [HttpGet("moderation")]
    public async Task<IActionResult> GetAdsForModeration()
    {
        if (!await IsAdminAsync()) return Forbid();

        var ads = await _dbContext.UserAds
            .Include(a => a.Images)
            .Include(a => a.User)
            .Where(a => a.Status == AdStatus.UnderModeration)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.Description,
                a.Price,
                a.CreatedAt,
                AuthorEmail = a.User.Email,
                AuthorName = a.User.Name,
                Images = a.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
            })
            .ToListAsync();

        return Ok(ads);
    }

    [HttpGet("moderation/count")]
    public async Task<IActionResult> GetModerationCount()
    {
        if (!await IsAdminAsync()) return Forbid();

        var count = await _dbContext.UserAds
            .Where(a => a.Status == AdStatus.UnderModeration)
            .CountAsync();

        return Ok(new { count });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveAd(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var ad = await _dbContext.UserAds.FindAsync(id);
        if (ad == null) return NotFound();

        ad.Status = AdStatus.Active;
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectAd(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var ad = await _dbContext.UserAds.FindAsync(id);
        if (ad == null) return NotFound();

        ad.Status = AdStatus.Closed;
        await _dbContext.SaveChangesAsync();

        return Ok();
    }
}
