using Microsoft.AspNetCore.Mvc;
using FedresursScraper.Services;
using Lots.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClassificationController : ControllerBase
    {
        private readonly IClassificationManager _classificationManager;
        private readonly LotsDbContext _dbContext;

        public ClassificationController(IClassificationManager classificationManager, LotsDbContext dbContext)
        {
            _classificationManager = classificationManager;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Запускает экспресс-оценку (классификацию) для указанного лота локально.
        /// </summary>
        /// <param name="lotId">ID лота</param>
        /// <returns>Результат постановки в очередь</returns>
        [HttpPost("{lotId:guid}/classify")]
        public async Task<IActionResult> ClassifyLot(Guid lotId)
        {
            var lot = await _dbContext.Lots.FirstOrDefaultAsync(l => l.Id == lotId);
            if (lot == null)
            {
                return NotFound(new { message = $"Лот с ID {lotId} не найден." });
            }

            if (string.IsNullOrWhiteSpace(lot.Description))
            {
                return BadRequest(new { message = "У лота отсутствует описание, классификация невозможна." });
            }

            // Добавляем в очередь на классификацию
            await _classificationManager.EnqueueClassificationAsync(lot.Id, lot.Description, "Manual/API");

            return Ok(new { message = $"Лот {lotId} успешно добавлен в очередь на классификацию." });
        }

        /// <summary>
        /// Запускает батчевую классификацию для списка лотов локально (до 10 лотов).
        /// </summary>
        /// <param name="lotIds">Список ID лотов</param>
        /// <returns>Результат выполнения батча</returns>
        [HttpPost("classify-batch")]
        public async Task<IActionResult> ClassifyBatchLots([FromBody] List<Guid> lotIds)
        {
            if (lotIds == null || !lotIds.Any())
            {
                return BadRequest(new { message = "Список лотов пуст." });
            }

            if (lotIds.Count > 10)
            {
                return BadRequest(new { message = "Максимальный размер батча - 10 лотов за один запрос." });
            }

            // Вызываем напрямую для синхронного тестирования батча
            // Внимание: может занять некоторое время
            await _classificationManager.ClassifyLotsBatchAsync(lotIds, "Manual/API Batch");

            return Ok(new { message = $"Запущена батчевая классификация для {lotIds.Count} лотов." });
        }
    }
}