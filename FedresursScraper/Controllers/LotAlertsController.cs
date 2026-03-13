using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using Lots.Data.Entities;
using FedresursScraper.Models.LotAlerts;
using System.Security.Claims;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LotAlertsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;

    public LotAlertsController(LotsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Вспомогательный метод для получения ID текущего авторизованного пользователя
    /// </summary>
    private Guid GetCurrentUserId()
    {
        // Предполагается, что вы используете стандартную аутентификацию (Identity/JWT)
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.Parse(userIdString!);
    }

    /// <summary>
    /// Проверка, имеет ли пользователь активный Pro-доступ
    /// </summary>
    private async Task<bool> CheckUserProStatusAsync(Guid userId)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return false;

        return user.IsSubscriptionActive &&
               (user.SubscriptionEndDate == null || user.SubscriptionEndDate > DateTime.UtcNow);
    }

    [HttpGet]
    public async Task<ActionResult<List<LotAlertDto>>> GetMyAlerts()
    {
        var userId = GetCurrentUserId();

        var alerts = await _dbContext.LotAlerts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new LotAlertDto
            {
                Id = a.Id,
                RegionCodes = a.RegionCodes,
                Categories = a.Categories,
                MinPrice = a.MinPrice,
                MaxPrice = a.MaxPrice,
                BiddingType = a.BiddingType,
                IsSharedOwnership = a.IsSharedOwnership,
                DeliveryTimeStr = a.DeliveryTimeStr,
                IsActive = a.IsActive,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(alerts);
    }

    [HttpPost]
    public async Task<ActionResult<LotAlertDto>> CreateAlert([FromBody] UpsertLotAlertRequest request)
    {
        var userId = GetCurrentUserId();

        // Проверяем Pro-доступ
        if (!await CheckUserProStatusAsync(userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Создание подписок доступно только пользователям с PRO-доступом.");
        }

        // Защита от дурака
        bool isAllCategories = request.Categories == null || request.Categories.Count() == 0;
        bool isAllRegions = request.RegionCodes == null || request.RegionCodes.Count() == 0;

        if (isAllCategories && isAllRegions)
        {
            return BadRequest(new
            {
                message = "Подписка слишком широкая. Укажите хотя бы одну категорию или регион."
            });
        }

        // Лимит на количество подписок (например, не больше 10 на человека)
        var currentAlertsCount = await _dbContext.LotAlerts.CountAsync(a => a.UserId == userId);
        if (currentAlertsCount >= 10)
        {
            return BadRequest("Достигнут лимит в 10 активных подписок.");
        }

        // Создаем сущность
        var alert = new LotAlert
        {
            UserId = userId,
            RegionCodes = request.RegionCodes?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToArray(),
            Categories = request.Categories?.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToArray(),
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            BiddingType = request.BiddingType,
            IsSharedOwnership = request.IsSharedOwnership,
            DeliveryTimeStr = request.DeliveryTimeStr,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.LotAlerts.Add(alert);
        await _dbContext.SaveChangesAsync();

        return Ok(new LotAlertDto
        {
            Id = alert.Id,
            RegionCodes = alert.RegionCodes,
            Categories = alert.Categories,
            MinPrice = alert.MinPrice,
            MaxPrice = alert.MaxPrice,
            BiddingType = alert.BiddingType,
            IsSharedOwnership = alert.IsSharedOwnership,
            DeliveryTimeStr = alert.DeliveryTimeStr,
            IsActive = alert.IsActive,
            CreatedAt = alert.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAlert(Guid id, [FromBody] UpsertLotAlertRequest request)
    {
        var userId = GetCurrentUserId();

        // Защита от дурака
        bool isAllCategories = request.Categories == null || request.Categories.Count() == 0;
        bool isAllRegions = request.RegionCodes == null || request.RegionCodes.Count() == 0;

        if (isAllCategories && isAllRegions)
        {
            return BadRequest(new
            {
                message = "Подписка слишком широкая. Укажите хотя бы одну категорию или регион."
            });
        }

        var alert = await _dbContext.LotAlerts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (alert == null) return NotFound();

        alert.RegionCodes = request.RegionCodes?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToArray();
        alert.Categories = request.Categories?.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToArray();
        alert.MinPrice = request.MinPrice;
        alert.MaxPrice = request.MaxPrice;
        alert.BiddingType = request.BiddingType;
        alert.IsSharedOwnership = request.IsSharedOwnership;
        alert.DeliveryTimeStr = request.DeliveryTimeStr;
        alert.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAlert(Guid id)
    {
        var userId = GetCurrentUserId();

        // Атомарное удаление (EF Core 7+)
        var rowsAffected = await _dbContext.LotAlerts
            .Where(a => a.Id == id && a.UserId == userId)
            .ExecuteDeleteAsync();

        if (rowsAffected == 0) return NotFound();

        return NoContent();
    }
}
