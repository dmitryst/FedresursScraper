using Ardalis.Specification.EntityFrameworkCore;
using FedresursScraper.Controllers.Models;
using FedresursScraper.Controllers.Utils;
using Lots.Data.Entities;
using Lots.Data.Specifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Lots.Data;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/voted-lots")]
[Authorize]
public class VotedLotsController : ControllerBase
{
    private readonly LotsDbContext _context;
    private readonly bool _aiQuickEvaluationAdminOnly;

    public VotedLotsController(LotsDbContext context, IConfiguration configuration)
    {
        _context = context;
        _aiQuickEvaluationAdminOnly = configuration.GetValue("Features:AiQuickEvaluationAdminOnly", true);
    }

    /// <summary>
    /// Возвращает пагинированный список лотов, за которые проголосовал пользователь.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaginatedResult<LotDto>>> GetVotedLots(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20
    )
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        // Получаем все ID лотов, за которые голосовал пользователь
        var allVotedLotIds = await _context.LotVotes
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => v.LotId)
            .ToListAsync();

        var totalCount = allVotedLotIds.Count;

        if (totalCount == 0)
        {
            return Ok(new PaginatedResult<LotDto>([], 0, 1, pageSize));
        }

        // Применяем пагинацию к списку ID
        var pagedLotIds = allVotedLotIds
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

        var lotDtos = sortedLots.Select(l => new LotDto
        {
            Id = l!.Id,
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
            Images = l.Images
                .OrderBy(i => i.Order)
                .Select(i => i.Url)
                .ToList(),
            PriceSchedules = l.PriceSchedules.Select(pc => new PriceScheduleDto
            {
                StartDate = pc.StartDate,
                EndDate = pc.EndDate,
                Price = pc.Price
            }),
            TradeStatus = l.TradeStatus,
            FinalPrice = l.FinalPrice,
            VotesCount = l.VotesCount,
            ViewCount = l.ViewCount
        }).ToList();

        if (_aiQuickEvaluationAdminOnly)
        {
            var showAiEvaluation = await IsAdminAsync();
            LotDtoAiEvaluationAccess.ApplyQuickEvaluationVisibility(lotDtos, showAiEvaluation);
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Ok(new PaginatedResult<LotDto>(lotDtos, totalCount, page, pageSize));
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

    private async Task<bool> IsAdminAsync() =>
        await AdminAccessHelper.IsAdminAsync(HttpContext, _context);
}
