using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/lots")]
    public class LotEvaluationController : ControllerBase
    {
        private readonly ILotEvaluationService _evaluationService;
        private readonly LotsDbContext _dbContext;

        public LotEvaluationController(ILotEvaluationService evaluationService, LotsDbContext dbContext)
        {
            _evaluationService = evaluationService;
            _dbContext = dbContext;
        }

        [HttpGet("{id}/evaluation")]
        public async Task<IActionResult> GetEvaluation(string id)
        {
            Guid lotId = Guid.Empty;

            if (Guid.TryParse(id, out Guid guid))
            {
                lotId = guid;
            }
            else if (int.TryParse(id, out int publicId))
            {
                var lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.PublicId == publicId);
                if (lot != null)
                {
                    lotId = lot.Id;
                }
            }

            if (lotId == Guid.Empty)
            {
                return BadRequest("Invalid ID");
            }

            // Ищем последнюю оценку для этого лота
            var evaluation = await _dbContext.LotEvaluations
                .Where(e => e.LotId == lotId)
                .OrderByDescending(e => e.CreatedAt) // Берем самую свежую
                .FirstOrDefaultAsync();

            if (evaluation == null)
            {
                return NotFound(); // Оценки нет
            }

            // Мапим в DTO результата (можно вынести в AutoMapper)
            var result = new
            {
                evaluation.EstimatedPrice,
                evaluation.LiquidityScore,
                evaluation.InvestmentSummary,
                evaluation.ReasoningText,
                // Другие поля если нужны
            };

            return Ok(result);
        }

        [HttpPost("{id}/evaluate")]
        public async Task<IActionResult> EvaluateLot(string id)
        {
            Guid lotId = Guid.Empty;

            // Пытаемся определить ID (Guid или PublicId)
            if (Guid.TryParse(id, out Guid guid))
            {
                lotId = guid;
            }
            else if (int.TryParse(id, out int publicId))
            {
                var lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.PublicId == publicId);
                if (lot != null)
                {
                    lotId = lot.Id;
                }
            }

            if (lotId == Guid.Empty)
            {
                return BadRequest("Invalid ID or Lot not found");
            }

            try
            {
                var result = await _evaluationService.EvaluateLotAsync(lotId);
                if (result == null)
                {
                    return NotFound("Лот не найден или оценка завершилась ошибкой");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
