using FedresursScraper.Services;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/[controller]")]
[AdminApiKey]
public class AdminSeoController : ControllerBase
{
    private readonly ILogger<AdminSeoController> _logger;
    private readonly LotsDbContext _dbContext;

    // Внедряем фабрику Scope вместо самих сервисов для фоновых задач
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminSeoController(
        ILogger<AdminSeoController> logger,
        LotsDbContext dbContext,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Возвращает тестовую выборку сгенерированных URL для старых лотов.
    /// </summary>
    [HttpGet("test-slugs")]
    public async Task<IActionResult> TestSlugs([FromQuery] int count = 50)
    {
        var lots = await _dbContext.Lots
            .Where(l => l.PublicId >= 10001 && l.PublicId <= 53158 && string.IsNullOrEmpty(l.Slug))
            .OrderBy(l => Guid.NewGuid()) // Берем случайные лоты для репрезентативности
            .Take(count)
            .Select(l => new { l.PublicId, l.Title, l.Description })
            .ToListAsync();

        var result = lots.Select(l =>
        {
            var textForSlug = !string.IsNullOrWhiteSpace(l.Title) ? l.Title : l.Description;
            var slug = SlugHelper.GenerateSlug(textForSlug ?? "lot");

            return new
            {
                l.PublicId,
                OriginalText = textForSlug?.Substring(0, Math.Min(textForSlug.Length, 100)), // Показываем начало текста
                GeneratedSlug = slug,
                Url = $"https://s-lot.ru/lot/{slug}-{l.PublicId}"
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Массово генерирует URL для всех исторических завершенных лотов 
    /// и отправляет их в IndexNow для обновления сниппетов в Яндексе.
    /// </summary>
    [HttpPost("submit-historical-archived-lots")]
    public IActionResult SubmitHistoricalArchivedLotsToIndexNow()
    {
        // Запускаем в фоновом потоке
        _ = Task.Run(async () => await ProcessHistoricalArchivedLotsAsync());

        return Accepted(new { Message = "Процесс массовой отправки исторических архивных лотов в IndexNow запущен. Проверяйте логи." });
    }

    private async Task ProcessHistoricalArchivedLotsAsync()
    {
        try
        {
            _logger.LogInformation("Начата сборка всех завершенных лотов для отправки в IndexNow...");

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
            var indexNowService = scope.ServiceProvider.GetRequiredService<IIndexNowService>();

            // Получаем массив конечных статусов из доменной модели
            var finalStatuses = Lot.FinalTradeStatuses;

            // Выбираем все лоты, статус которых находится в списке конечных.
            // Используем AsNoTracking() для экономии памяти, так как лотов может быть очень много.
            var lotsData = await dbContext.Lots
                .AsNoTracking()
                .Where(l => l.TradeStatus != null && finalStatuses.Contains(l.TradeStatus))
                .Select(l => new { l.PublicId, l.Slug, l.Title, l.Description })
                .ToListAsync();

            if (lotsData.Count == 0)
            {
                _logger.LogInformation("Завершенных лотов для отправки не найдено.");
                return;
            }

            var urlsToSubmit = new List<string>(lotsData.Count);

            foreach (var lot in lotsData)
            {
                var slug = lot.Slug;

                if (string.IsNullOrWhiteSpace(slug))
                {
                    var textForSlug = !string.IsNullOrWhiteSpace(lot.Title) ? lot.Title : lot.Description;
                    slug = SlugHelper.GenerateSlug(textForSlug ?? "lot");
                }

                urlsToSubmit.Add($"https://s-lot.ru/lot/{slug}-{lot.PublicId}");
            }

            var uniqueUrls = urlsToSubmit.Distinct().ToList();
            _logger.LogInformation("Сгенерировано {Count} уникальных URL завершенных лотов. Начинаем отправку...", uniqueUrls.Count);

            // Лимит Яндекса - 10 000 URL за раз. Отправляем батчами по 5000.
            const int batchSize = 5000;
            for (int i = 0; i < uniqueUrls.Count; i += batchSize)
            {
                var batch = uniqueUrls.Skip(i).Take(batchSize).ToList();
                _logger.LogInformation("Отправка батча в IndexNow: с {Start} по {End} из {Total}...",
                    i + 1, i + batch.Count, uniqueUrls.Count);

                await indexNowService.SubmitUrlsAsync(batch);

                // Делаем паузу в 3 секунды, чтобы не словить Rate Limit от API Яндекса
                await Task.Delay(3000);
            }

            _logger.LogInformation("Массовая отправка исторических архивных лотов успешно завершена!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при массовой отправке завершенных лотов в IndexNow.");
        }
    }
}
