using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Lots.Data;
using Lots.Data.Entities;

namespace FedresursScraper.Services.LotAlerts;

/// <summary>
/// Фоновый процесс, который ищет новые классифицированные лоты 
/// и сопоставляет их с активными оповещениями (алертами) Pro-пользователей.
/// </summary>
public class LotAlertMatchingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LotAlertMatchingWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(20); // Как часто проверять новые лоты

    // Сохраняем время последней проверки, чтобы не брать одни и те же лоты дважды
    private DateTime _lastCheckTime = DateTime.UtcNow.AddHours(-1);

    public LotAlertMatchingWorker(IServiceProvider serviceProvider, ILogger<LotAlertMatchingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LotAlertMatchingWorker запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

                var now = DateTime.UtcNow;

                // Получаем свежие лоты, которые были успешно классифицированы с момента прошлой проверки
                // Мы берем их из таблицы состояний, используя навигационное свойство Lot.
                var freshLots = await dbContext.LotClassificationStates
                    .AsNoTracking()
                    .Include(s => s.Lot)
                        .ThenInclude(l => l.Categories)
                    .Where(s => s.Status == ClassificationStatus.Success && s.Lot.CreatedAt > _lastCheckTime)
                    .Select(s => s.Lot)
                    .ToListAsync(stoppingToken);

                if (freshLots.Any())
                {
                    // Загружаем все активные алерты вместе с проверкой PRO-доступа (User)
                    var activeAlerts = await dbContext.LotAlerts
                        .AsNoTracking()
                        .Include(a => a.User)
                        .Where(a => a.IsActive
                                    && a.User.HasProAccess)
                        .ToListAsync(stoppingToken);

                    // Получаем ID всех свежих лотов
                    var freshLotIds = freshLots.Select(l => l.Id).ToList();

                    // Ищем в БД уже созданные совпадения для этих лотов (чтобы не дублировать после рестарта)
                    var existingMatchesList = await dbContext.LotAlertMatches
                        .AsNoTracking()
                        .Where(m => freshLotIds.Contains(m.LotId))
                        .Select(m => new { m.LotId, m.LotAlertId })
                        .ToListAsync(stoppingToken);

                    // Кладем их в HashSet (Кортеж: LotId + LotAlertId) для быстрого поиска
                    var existingMatchSet = existingMatchesList
                        .Select(m => (m.LotId, m.LotAlertId))
                        .ToHashSet();

                    var newMatches = new List<LotAlertMatch>();

                    // In-Memory Matching (Быстрое сопоставление в памяти)
                    foreach (var lot in freshLots)
                    {
                        var lotCategoryNames = lot.Categories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                        foreach (var alert in activeAlerts)
                        {
                            // Проверка региона (если в алерте заданы регионы)
                            if (alert.RegionCodes != null && alert.RegionCodes.Any() &&
                                !alert.RegionCodes.Contains(lot.PropertyRegionCode))
                            {
                                continue; // Регион не совпал
                            }

                            // Проверка категорий (если в алерте заданы категории)
                            if (alert.Categories != null && alert.Categories.Any() &&
                                !alert.Categories.Any(ac => lotCategoryNames.Contains(ac)))
                            {
                                continue; // Категория не совпала
                            }

                            // Проверка цены (если задана)
                            if (alert.MinPrice.HasValue && lot.StartPrice < alert.MinPrice.Value) continue;
                            if (alert.MaxPrice.HasValue && lot.StartPrice > alert.MaxPrice.Value) continue;

                            // Проверка вида торгов
                            // Если в алерте указан конкретный тип торгов (и он не пустой), а у лота тип другой -> пропускаем
                            if (!string.IsNullOrEmpty(alert.BiddingType) &&
                                !string.Equals(alert.BiddingType, lot.Bidding?.Type, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // Проверка доли
                            // Если в алерте явно указано Искать только доли (true) или Искать только целиком (false)
                            if (alert.IsSharedOwnership.HasValue && alert.IsSharedOwnership.Value != lot.IsSharedOwnership)
                            {
                                continue;
                            }

                            // Проверка на дубликат
                            if (existingMatchSet.Contains((lot.Id, alert.Id)))
                            {
                                continue; // Такое совпадение уже было записано в БД до рестарта парсера
                            }

                            // Если все проверки пройдены и это не дубликат, создаем Match
                            newMatches.Add(new LotAlertMatch
                            {
                                LotAlertId = alert.Id,
                                LotId = lot.Id,
                                IsSent = false,
                                CreatedAt = now
                            });
                        }
                    }

                    // Сохраняем найденные совпадения в БД (Outbox)
                    if (newMatches.Any())
                    {
                        dbContext.LotAlertMatches.AddRange(newMatches);
                        await dbContext.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation("Найдено {Count} совпадений по подпискам для {LotCount} новых лотов.",
                            newMatches.Count, freshLots.Count);
                    }
                }

                _lastCheckTime = now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при поиске совпадений по алертам.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}
