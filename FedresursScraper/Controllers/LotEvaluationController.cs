using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Lots.Data.Entities;

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

        /// <summary>
        /// Получить оценку. 
        /// Возвращает данные ТОЛЬКО если пользователь уже "покупал" (запускал) анализ этого лота.
        /// </summary>
        [Authorize]
        [HttpGet("{id}/evaluation")]
        public async Task<IActionResult> GetEvaluation(string id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var lotId = await ResolveLotId(id);
            if (lotId == Guid.Empty)
            {
                return BadRequest("Invalid ID");
            }

            // Проверяем, запускал ли этот пользователь анализ по этому лоту ранее
            var hasAccess = await _dbContext.LotEvaluationUserRunStatistics
                .AnyAsync(r => r.UserId == userId && r.LotId == lotId);

            if (!hasAccess)
            {
                // Важно: возвращаем 404, если доступ не куплен. 
                // Фронт поймет это как "Нужно показать кнопку"
                return NotFound(new { message = "Анализ лотов не оплачен" });
            }

            // Если доступ есть, отдаем оценку
            var evaluation = await _dbContext.LotEvaluations
                .Where(e => e.LotId == lotId)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();

            if (evaluation == null)
            {
                return NotFound("Оценка отсутствует");
            }

            var result = new
            {
                evaluation.EstimatedPrice,
                evaluation.LiquidityScore,
                evaluation.InvestmentSummary,
                evaluation.ReasoningText,
            };

            return Ok(result);
        }

        // <summary>
        /// Запустить анализ (или вернуть купленный).
        /// Списывает лимит ТОЛЬКО если это первый запуск для этого лота.
        /// </summary>
        [Authorize]
        [HttpPost("{id}/evaluate")]
        public async Task<IActionResult> EvaluateLot(string id)
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var lotId = await ResolveLotId(id);
            if (lotId == Guid.Empty)
            {
                return BadRequest("Некорректный Id или лот не найден");
            }

            try
            {
                // ПРОВЕРКА: Запускал ли пользователь анализ этого лота ранее?
                var hasRunEvaluationBefore = await _dbContext.LotEvaluationUserRunStatistics
                    .AnyAsync(r => r.UserId == userId && r.LotId == lotId);

                // Если не запускал — проверяем лимиты (но пока не списываем их)
                if (!hasRunEvaluationBefore)
                {
                    // Проверяем квоту: считаем запуски за текущий месяц
                    var nowUtc = DateTime.UtcNow;
                    var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var nextMonthStartUtc = monthStartUtc.AddMonths(1);

                    var runsThisMonth = await _dbContext.LotEvaluationUserRunStatistics
                        .Where(r => r.UserId == userId
                                    && r.CreatedAt >= monthStartUtc
                                    && r.CreatedAt < nextMonthStartUtc)
                        .CountAsync();

                    // Если лимит исчерпан (>= 3), проверяем подписку
                    if (runsThisMonth >= 3)
                    {
                        var user = await _dbContext.Users.FindAsync(userId);
                        if (user == null)
                        {
                            return NotFound(new { message = "Пользователь не найден" });
                        }

                        // Проверяем активную подписку
                        bool hasActiveSubscription = user.IsSubscriptionActive
                                                      && user.SubscriptionEndDate.HasValue
                                                      && user.SubscriptionEndDate.Value > nowUtc;

                        if (!hasActiveSubscription)
                        {
                            return StatusCode(402, new
                            {
                                message = "Исчерпан бесплатный лимит (3 анализа в месяц). Оформите Pro-подписку для неограниченного доступа.",
                                limitReached = true,
                                actionUrl = "/subscribe"
                            });
                        }
                    }
                }

                // Проверяем, есть ли уже детальная оценка для этого лота (кэш)
                var existingEvaluation = await _dbContext.LotEvaluations
                    .Where(e => e.LotId == lotId)
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync();

                // Сценарий А: Оценка уже есть в БД (от другого пользователя или старая)
                // возвращаем из кэша результат оценки, не делая запрос в DeepSeek
                if (existingEvaluation != null)
                {
                    // Списываем квоту сейчас (так как результат мы точно отдадим)
                    if (!hasRunEvaluationBefore)
                    {
                        await RecordUserRun(userId, lotId);
                    }
                    var cachedResult = new
                    {
                        existingEvaluation.EstimatedPrice,
                        existingEvaluation.LiquidityScore,
                        existingEvaluation.InvestmentSummary,
                        existingEvaluation.ReasoningText,
                        fromCache = true // Флаг для фронта (опционально)
                    };

                    return Ok(cachedResult);
                }

                // Сценарий Б: Оценки нет, идём в DeepSeek
                var result = await _evaluationService.EvaluateLotAsync(lotId);
                if (result == null)
                {
                    return NotFound("Лот не найден или оценка завершилась ошибкой");
                }

                if (!hasRunEvaluationBefore)
                {
                    await RecordUserRun(userId, lotId);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // Вспомогательные методы
        private Guid GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                     ?? User.FindFirst("sub")
                     ?? User.FindFirst("id");
            return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
        }

        private async Task<Guid> ResolveLotId(string id)
        {
            if (Guid.TryParse(id, out Guid guid))
                return guid;

            if (int.TryParse(id, out int publicId))
            {
                var lot = await _dbContext.Lots.Select(l => new { l.Id, l.PublicId }).FirstOrDefaultAsync(l => l.PublicId == publicId);
                return lot?.Id ?? Guid.Empty;
            }
            return Guid.Empty;
        }

        private async Task RecordUserRun(Guid userId, Guid lotId)
        {
            var run = new LotEvaluationUserRunStatistics
            {
                UserId = userId,
                LotId = lotId,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.LotEvaluationUserRunStatistics.Add(run);
            await _dbContext.SaveChangesAsync();
        }
    }
}
