using FedresursScraper.Controllers.Utils;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/lots/{lotId}/vote")]
[Authorize]
public class LotVotesController : ControllerBase
{
    private readonly LotsDbContext _context;

    public LotVotesController(LotsDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> ToggleVote(Guid lotId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var lot = await _context.Lots.FirstOrDefaultAsync(l => l.Id == lotId);
        if (lot == null) return NotFound(new { message = "Лот не найден" });

        var existingVote = await _context.LotVotes
            .FirstOrDefaultAsync(v => v.UserId == userId && v.LotId == lotId);

        if (existingVote != null)
        {
            _context.LotVotes.Remove(existingVote);
            lot.VotesCount = Math.Max(0, lot.VotesCount - 1);
            await _context.SaveChangesAsync();
            return Ok(new { isVoted = false, votesCount = lot.VotesCount });
        }
        else
        {
            // Проверяем лимиты
            var activeVotesCount = await _context.LotVotes.CountAsync(v => v.UserId == userId.Value);
            
            var user = await _context.Users.FindAsync(userId.Value);
            bool isPro = user != null && user.IsSubscriptionActive && user.SubscriptionEndDate.HasValue && user.SubscriptionEndDate.Value > DateTime.UtcNow;

            int limit = isPro ? 10 : 3;

            if (activeVotesCount >= limit)
            {
                return BadRequest(new { 
                    message = $"Вы исчерпали лимит одновременных голосов ({limit}). Чтобы проголосовать за этот лот, отмените свой голос за другой." 
                });
            }

            var vote = new LotVote { UserId = userId.Value, LotId = lotId };
            _context.LotVotes.Add(vote);
            lot.VotesCount += 1;
            await _context.SaveChangesAsync();
            return Ok(new { isVoted = true, votesCount = lot.VotesCount });
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetVoteStatus(Guid lotId)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Ok(new { isVoted = false });

        var existingVote = await _context.LotVotes
            .AnyAsync(v => v.UserId == userId && v.LotId == lotId);

        return Ok(new { isVoted = existingVote });
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
