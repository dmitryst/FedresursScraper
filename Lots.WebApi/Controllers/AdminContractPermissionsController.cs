using Lots.Data;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/admin/contract-permissions")]
[Authorize]
public class AdminContractPermissionsController : ControllerBase
{
    private readonly LotsDbContext _dbContext;

    public AdminContractPermissionsController(LotsDbContext dbContext)
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

    [HttpGet]
    public async Task<IActionResult> GetPermissions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await IsAdminAsync()) return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.UserLotContractPermissions.AsNoTracking();

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.UserId,
                UserEmail = p.User.Email,
                UserName = p.User.Name,
                p.LotId,
                LotPublicId = p.Lot.PublicId,
                LotTitle = p.Lot.Title,
                LotStartPrice = p.Lot.StartPrice,
                p.FixedRewardAmount,
                p.SuccessRewardAmount,
                p.CreatedAt
            })
            .ToListAsync();

        var result = items.Select(p =>
        {
            var slug = !string.IsNullOrWhiteSpace(p.LotTitle)
                ? SlugHelper.GenerateSlug(p.LotTitle)
                : "lot";

            return new
            {
                p.Id,
                p.UserId,
                p.UserEmail,
                p.UserName,
                p.LotId,
                p.LotPublicId,
                p.LotTitle,
                p.LotStartPrice,
                p.FixedRewardAmount,
                p.SuccessRewardAmount,
                p.CreatedAt,
                LotUrl = $"/lot/{slug}-{p.LotPublicId}"
            };
        });

        return Ok(new
        {
            items = result,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreatePermission([FromBody] CreateContractPermissionRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == request.UserEmail.Trim());

        if (user == null)
        {
            return NotFound(new { message = "Пользователь с таким email не найден." });
        }

        var lot = await _dbContext.Lots
            .FirstOrDefaultAsync(l => l.PublicId == request.LotPublicId);

        if (lot == null)
        {
            return NotFound(new { message = "Лот с таким ID не найден." });
        }

        var exists = await _dbContext.UserLotContractPermissions
            .AnyAsync(p => p.UserId == user.Id && p.LotId == lot.Id);

        if (exists)
        {
            return Conflict(new { message = "Разрешение для этого пользователя и лота уже существует." });
        }

        var permission = new UserLotContractPermission
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            LotId = lot.Id,
            FixedRewardAmount = request.FixedRewardAmount,
            SuccessRewardAmount = request.SuccessRewardAmount,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.UserLotContractPermissions.Add(permission);
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            permission.Id,
            UserId = user.Id,
            UserEmail = user.Email,
            UserName = user.Name,
            LotId = lot.Id,
            LotPublicId = lot.PublicId,
            LotTitle = lot.Title,
            permission.FixedRewardAmount,
            permission.SuccessRewardAmount,
            permission.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePermission(Guid id, [FromBody] UpdateContractPermissionRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var permission = await _dbContext.UserLotContractPermissions.FindAsync(id);
        if (permission == null) return NotFound();

        permission.FixedRewardAmount = request.FixedRewardAmount;
        permission.SuccessRewardAmount = request.SuccessRewardAmount;
        await _dbContext.SaveChangesAsync();

        return Ok(new { permission.Id, permission.FixedRewardAmount, permission.SuccessRewardAmount });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePermission(Guid id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var permission = await _dbContext.UserLotContractPermissions.FindAsync(id);
        if (permission == null) return NotFound();

        _dbContext.UserLotContractPermissions.Remove(permission);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }
}

public class CreateContractPermissionRequest
{
    [Required]
    [EmailAddress]
    public string UserEmail { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int LotPublicId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Сумма фиксированного вознаграждения должна быть больше 0.")]
    public decimal FixedRewardAmount { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Сумма вознаграждения при победе должна быть больше 0.")]
    public decimal SuccessRewardAmount { get; set; }
}

public class UpdateContractPermissionRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Сумма фиксированного вознаграждения должна быть больше 0.")]
    public decimal FixedRewardAmount { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Сумма вознаграждения при победе должна быть больше 0.")]
    public decimal SuccessRewardAmount { get; set; }
}
