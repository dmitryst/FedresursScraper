using Microsoft.AspNetCore.Mvc;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize(Roles = "Admin")] // Рекомендуется включить авторизацию
    public class AdminController : ControllerBase
    {
        private readonly IRosreestrService _rosreestrService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IRosreestrService rosreestrService, ILogger<AdminController> logger)
        {
            _rosreestrService = rosreestrService;
            _logger = logger;
        }

        /// <summary>
        /// Принудительно запускает повторную обработку (reprocess) очереди кадастровых номеров,
        /// по которым ранее не удалось получить координаты (из-за ошибок 500/timeout).
        /// </summary>
        /// <returns>Статистика обработки: количество успешно обработанных и детали.</returns>
        [HttpPost("reprocess-rosreestr")]
        public async Task<IActionResult> ReprocessRosreestr()
        {
            var initialQueueSize = _rosreestrService.GetQueueSize();

            if (initialQueueSize == 0)
            {
                return Ok(new { Message = "Очередь репроцессинга пуста. Действий не требуется." });
            }

            _logger.LogInformation("Администратор запустил репроцессинг Росреестра. В очереди: {Count}", initialQueueSize);

            // Запускаем процесс
            var result = await _rosreestrService.ReprocessRetryQueueAsync();

            var finalQueueSize = _rosreestrService.GetQueueSize();

            return Ok(new
            {
                Message = "Репроцессинг завершен",
                QueueSizeBefore = initialQueueSize,
                QueueSizeAfter = finalQueueSize, // Если сервис все еще лежит, это число будет равно result.Failed.Count
                Stats = new
                {
                    TotalProcessed = result.TotalProcessed,
                    SuccessCount = result.Succeeded.Count,
                    FailureCount = result.Failed.Count
                },
                // Полные списки для детального анализа
                Details = result
            });
        }

        /// <summary>
        /// Получить текущий размер очереди на репроцессинг.
        /// </summary>
        [HttpGet("rosreestr-queue-size")]
        public IActionResult GetRosreestrQueueSize()
        {
            var count = _rosreestrService.GetQueueSize();
            return Ok(new { QueueSize = count });
        }
    }
}
