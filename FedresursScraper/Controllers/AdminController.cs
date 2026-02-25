using FedresursScraper.Services;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize(Roles = "Admin")] // Рекомендуется включить авторизацию
    public class AdminController : ControllerBase
    {
        private readonly IRosreestrService _rosreestrService;
        private readonly IClassificationQueue _classificationQueue;
        private readonly LotsDbContext _dbContext;
        private readonly ILogger<AdminController> _logger;
        private readonly IRosreestrQueue _rosreestrQueue;

        public AdminController(
            IRosreestrService rosreestrService,
            IClassificationQueue classificationQueue,
            LotsDbContext dbContext,
            ILogger<AdminController> logger,
            IRosreestrQueue rosreestrQueue)
        {
            _rosreestrService = rosreestrService;
            _classificationQueue = classificationQueue;
            _dbContext = dbContext;
            _logger = logger;
            _rosreestrQueue = rosreestrQueue;
        }

        /// <summary>
        /// Получить текущий размер очереди на классификацию (DeepSeek).
        /// </summary>
        [HttpGet("classification-queue-size")]
        public IActionResult GetClassificationQueueSize()
        {
            var count = _classificationQueue.GetQueueSize();
            return Ok(new { QueueSize = count });
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

        [HttpPost("lots/{publicId:int}/refresh-rosreestr")]
        public async Task<IActionResult> RefreshRosreestrData(int publicId)
        {
            var lot = await _dbContext.Lots
                .Include(l => l.CadastralNumbers)
                .FirstOrDefaultAsync(l => l.PublicId == publicId);

            if (lot == null) return NotFound(new { Message = $"Лот {publicId} не найден." });

            var numbers = lot.CadastralNumbers?
                .Select(x => x.CadastralNumber)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList() ?? [];

            var lotId = lot.Id;

            if (numbers.Count == 0) return BadRequest(new { Message = $"У лота {lotId} нет кадастровых номеров." });

            await _rosreestrQueue.QueueWorkItemAsync(async (serviceProvider, token) =>
            {
                var rosreestrService = serviceProvider.GetRequiredService<IRosreestrService>();
                var scopedLogger = serviceProvider.GetRequiredService<ILogger<AdminController>>();

                try
                {
                    await rosreestrService.EnrichLotWithRosreestrDataAsync(lotId, numbers, forceUpdateCoordinates: true, token);
                }
                catch (Exception ex)
                {
                    scopedLogger.LogError(ex, "Admin: Ошибка при ручном обновлении Росреестра для лота {LotId}.", lotId);
                }
            });

            return Accepted(new { Message = $"Задача поставлена в очередь.", LotId = lotId, CadastralNumbers = numbers });
        }


        /// <summary>
        /// Сбрасывает флаг IsEnriched для торгов МЭТС, чтобы сервис обогащения снова обработал их.
        /// Полезно, когда фото появились на сайте после первоначального обогащения.
        /// </summary>
        /// <param name="tradeNumber">Номер торгов (опционально). Если не указан, сбрасывает для всех торгов МЭТС без фото.</param>
        /// <param name="fromDate">Дата начала периода (опционально). Фильтрует торги по дате создания (CreatedAt).</param>
        /// <param name="toDate">Дата окончания периода (опционально). Фильтрует торги по дате создания (CreatedAt).</param>
        /// <param name="fromPublicId">Начальный PublicId лота (опционально). Фильтрует торги, содержащие лоты с PublicId >= fromPublicId.</param>
        /// <param name="toPublicId">Конечный PublicId лота (опционально). Фильтрует торги, содержащие лоты с PublicId <= toPublicId.</param>
        [HttpPost("reset-mets-enrichment")]
        public async Task<IActionResult> ResetMetsEnrichment(
            [FromQuery] string? tradeNumber = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? fromPublicId = null,
            [FromQuery] int? toPublicId = null)
        {
            try
            {
                var query = _dbContext.Biddings
                    .Include(b => b.Lots)
                    .Include(b => b.EnrichmentState)
                    .Where(b => b.Platform.Contains("Межрегиональная Электронная Торговая Система"))
                    .Where(b => b.IsEnriched == true);

                // Если указан номер торгов, фильтруем по нему
                if (!string.IsNullOrEmpty(tradeNumber))
                {
                    query = query.Where(b => b.TradeNumber == tradeNumber || b.TradeNumber.StartsWith(tradeNumber));
                }

                // Фильтр по дате создания торгов
                if (fromDate.HasValue)
                {
                    var fromDateUtc = fromDate.Value.ToUniversalTime();
                    query = query.Where(b => b.CreatedAt >= fromDateUtc);
                }

                if (toDate.HasValue)
                {
                    var toDateUtc = toDate.Value.ToUniversalTime();
                    // Добавляем один день, чтобы включить весь день toDate
                    var toDateUtcEnd = toDateUtc.Date.AddDays(1).AddTicks(-1);
                    query = query.Where(b => b.CreatedAt <= toDateUtcEnd);
                }

                // Фильтр по PublicId лотов
                // Если указан диапазон publicId, находим торги, у которых есть хотя бы один лот в этом диапазоне
                if (fromPublicId.HasValue || toPublicId.HasValue)
                {
                    var lotQuery = _dbContext.Lots.AsQueryable();

                    if (fromPublicId.HasValue)
                    {
                        lotQuery = lotQuery.Where(l => l.PublicId >= fromPublicId.Value);
                    }

                    if (toPublicId.HasValue)
                    {
                        lotQuery = lotQuery.Where(l => l.PublicId <= toPublicId.Value);
                    }

                    // Получаем ID торгов, которые содержат лоты в указанном диапазоне publicId
                    var biddingIds = await lotQuery
                        .Select(l => l.BiddingId)
                        .Distinct()
                        .ToListAsync();

                    query = query.Where(b => biddingIds.Contains(b.Id));
                }

                var biddings = await query.ToListAsync();

                if (!biddings.Any())
                {
                    return Ok(new
                    {
                        Message = "Торги не найдены для сброса",
                        Count = 0
                    });
                }

                int resetCount = 0;
                foreach (var bidding in biddings)
                {
                    // Проверяем, есть ли фото у лотов
                    var hasAnyImages = bidding.Lots.Any(lot => lot.Images != null && lot.Images.Any());

                    // Сбрасываем только если нет фото (или если явно указан tradeNumber)
                    if (!hasAnyImages || !string.IsNullOrEmpty(tradeNumber))
                    {
                        bidding.IsEnriched = null;
                        bidding.EnrichedAt = null;

                        // Сбрасываем счетчики в EnrichmentState, чтобы сервис мог снова обработать
                        if (bidding.EnrichmentState != null)
                        {
                            bidding.EnrichmentState.MissingImagesAttemptCount = 0;
                            bidding.EnrichmentState.RetryCount = 0;
                            bidding.EnrichmentState.LastError = null;
                        }

                        resetCount++;
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Сброшен флаг IsEnriched для {Count} торгов МЭТС. TradeNumber: {TradeNumber}, FromDate: {FromDate}, ToDate: {ToDate}, FromPublicId: {FromPublicId}, ToPublicId: {ToPublicId}",
                    resetCount,
                    tradeNumber ?? "все",
                    fromDate,
                    toDate,
                    fromPublicId,
                    toPublicId);

                return Ok(new
                {
                    Message = "Флаг IsEnriched успешно сброшен",
                    TotalFound = biddings.Count,
                    ResetCount = resetCount,
                    Filters = new
                    {
                        TradeNumber = tradeNumber,
                        FromDate = fromDate,
                        ToDate = toDate,
                        FromPublicId = fromPublicId,
                        ToPublicId = toPublicId
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сбросе флага IsEnriched для торгов МЭТС");
                return StatusCode(500, new { Error = "Внутренняя ошибка сервера", Message = ex.Message });
            }
        }

        /// <summary>
        /// Одноразовый скрипт (эндпоинт) для инициализации расписания парсинга (NextStatusCheckAt) для исторических торгов.
        /// 
        /// Проходит по всем нефинализированным торгам с пустым расписанием и вычисляет дату следующей проверки 
        /// с помощью доменной логики сущности Bidding. Для торгов, проверка которых отложена на стандартные 7 дней, 
        /// добавляет случайный разброс (jitter) от 0 до 7 дней. 
        /// 
        /// Это необходимо для "размазывания" нагрузки на фоновый сервис и целевую площадку (Федресурс), 
        /// чтобы избежать пиковой отправки десятков тысяч запросов в первый день запуска обновленного сервиса.
        /// После успешного применения на проде этот метод можно будет удалить.
        /// </summary>

        [HttpPost("init-schedules")]
        public async Task<IActionResult> InitBiddingSchedules(CancellationToken token)
        {
            var now = DateTime.UtcNow;
            var batchSize = 1000;
            int updatedCount = 0;
            var random = new Random();

            while (true)
            {
                // Берем батч торгов, у которых расписание еще не задано
                var biddings = await _dbContext.Biddings
                    .Where(b => !b.IsTradeStatusesFinalized && b.NextStatusCheckAt == null)
                    .Take(batchSize)
                    .ToListAsync(token);

                if (biddings.Count == 0) break;

                foreach (var bidding in biddings)
                {
                    // Используем вашу новую доменную логику
                    bidding.ScheduleNextCheck(now);

                    // Распределяем нагрузку для старых зависших торгов.
                    // Если логика решила отложить проверку на 7 дней (торги идут), 
                    // мы размазываем этот первый запуск случайным образом от 0 до 7 дней, 
                    // чтобы не создать "ударную волну" из 30 000 запросов ровно через неделю.
                    if (bidding.NextStatusCheckAt.HasValue && bidding.NextStatusCheckAt.Value.Date == now.AddDays(7).Date)
                    {
                        int randomDays = random.Next(0, 8); // от 0 до 7
                        bidding.NextStatusCheckAt = now.AddDays(randomDays);
                    }
                }

                await _dbContext.SaveChangesAsync(token);
                updatedCount += biddings.Count;
            }

            return Ok(new { message = $"Успешно инициализировано расписание для {updatedCount} торгов." });
        }
    }
}
