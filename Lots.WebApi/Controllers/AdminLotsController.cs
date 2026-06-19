using Lots.Data.Entities;
using Lots.Application.Services.VehicleNormalization;
using Lots.Application.Interfaces;
using FedresursScraper.Services;
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
    private readonly IVehicleAttributesAdminService _vehicleAttributesAdminService;
    private readonly ILotDescriptionAlignmentService _alignmentService;

    public AdminLotsController(
        LotsDbContext dbContext,
        IVehicleAttributesAdminService vehicleAttributesAdminService,
        ILotDescriptionAlignmentService alignmentService)
    {
        _dbContext = dbContext;
        _vehicleAttributesAdminService = vehicleAttributesAdminService;
        _alignmentService = alignmentService;
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
                Platform = l.Bidding.Platform,
                BankruptMessageId = l.Bidding.BankruptMessageId,
                ViewingProcedure = l.Bidding.ViewingProcedure
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
                BankruptMessageId = l.BankruptMessageId == Guid.Empty ? (Guid?)null : l.BankruptMessageId,
                l.ViewingProcedure,
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

    public class AlignPreviewRequest
    {
        public List<int> PublicIds { get; set; } = [];
    }

    /// <summary>
    /// Предпросмотр выравнивания описания лота с данными Федресурса.
    /// </summary>
    [HttpPost("needs-description/align-preview")]
    public async Task<IActionResult> PreviewDescriptionAlignment(
        [FromBody] AlignPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (request.PublicIds == null || request.PublicIds.Count == 0)
            return BadRequest(new { message = "Укажите publicIds лотов." });

        try
        {
            var previews = await _alignmentService.PreviewAsync(request.PublicIds, cancellationToken);
            return Ok(new { items = previews });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Применить выровненное описание после ручного подтверждения.
    /// </summary>
    [HttpPost("needs-description/align-apply")]
    public async Task<IActionResult> ApplyDescriptionAlignment(
        [FromBody] ApplyLotDescriptionAlignmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!await IsAdminAsync()) return Forbid();

        if (request.PublicId <= 0 || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { message = "Некорректные данные." });

        try
        {
            var result = await _alignmentService.ApplyAsync(request, cancellationToken);
            if (result == null)
                return NotFound(new { message = "Лот не найден или не требует доработки описания." });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Лоты «Легковой автомобиль» с маркой или моделью вне справочника.
    /// </summary>
    [HttpGet("unmatched-vehicle-attributes")]
    public async Task<IActionResult> GetUnmatchedVehicleAttributes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync()) return Forbid();

        var (items, totalCount) = await _vehicleAttributesAdminService.GetUnmatchedLotsAsync(
            page,
            pageSize,
            activeOnly,
            cancellationToken);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("unmatched-vehicle-attributes/count")]
    public async Task<IActionResult> GetUnmatchedVehicleAttributesCount(
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync()) return Forbid();

        var count = await _vehicleAttributesAdminService.GetUnmatchedLotsCountAsync(activeOnly, cancellationToken);
        return Ok(new { count });
    }

    /// <summary>
    /// Исправить или сбросить марку/модель лота (значения из справочника).
    /// </summary>
    [HttpPatch("{publicId:int}/vehicle-attributes")]
    public async Task<IActionResult> UpdateLotVehicleAttributes(
        int publicId,
        [FromBody] UpdateLotVehicleAttributesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync()) return Forbid();

        var result = await _vehicleAttributesAdminService.UpdateLotVehicleAttributesAsync(
            publicId,
            request,
            cancellationToken);

        if (result == null)
        {
            return NotFound(new { message = $"Лот {publicId} не найден или не является легковым автомобилем." });
        }

        return Ok(result);
    }
}
