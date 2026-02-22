using System.Text.RegularExpressions;
using FedresursScraper.Services;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FedresursScraper.BackgroundServices;

/// <summary>
/// Фоновый сервис для автоматического обновления результатов торгов по лотам.
/// 
/// Запускается по расписанию (по умолчанию в 02:00 ночи) и обрабатывает торги порционно (батчами).
/// 
/// Логика обработки каждых торгов:
/// - Выбирает только незавершенные торги (<see cref="Bidding.IsTradeStatusesFinalized"/> = false),
///   у которых отсутствует <see cref="Bidding.TradePeriod"/> и есть хотя бы один лот с номером.
/// - Парсит статусы лотов с площадки old.bankrot.fedresurs.ru через <see cref="ITradeCardLotsStatusScraper"/>.
/// - Для лотов, по которым удалось получить данные, сохраняет:
///   <see cref="Lot.TradeStatus"/>, <see cref="Lot.FinalPrice"/>, 
///   <see cref="Lot.WinnerName"/>, <see cref="Lot.WinnerInn"/>.
/// - Для лотов, которые не удалось найти на площадке (missing), проставляет технический статус
///   <c>"Торги завершены (нет данных)"</c>, чтобы исключить их из активной выборки.
/// - Если все лоты торгов перешли в конечный статус 
///   ("Завершенные", "Торги отменены", "Торги не состоялись", "Торги завершены (нет данных)"),
///   помечает торги как финализированные (<see cref="Bidding.IsTradeStatusesFinalized"/> = true),
///   исключая их из последующих запусков.
/// 
/// Управляется через конфигурацию (секция <c>BackgroundServices:TradeStatusesUpdate</c>):
/// <list type="bullet">
///   <item><term>Enabled</term><description>Включить/выключить сервис (по умолчанию: false).</description></item>
///   <item><term>RunOnStartup</term><description>Запустить парсинг немедленно при старте приложения (по умолчанию: true).</description></item>
///   <item><term>RunAtHour</term><description>Час запуска по расписанию, 0–23 (по умолчанию: 2).</description></item>
///   <item><term>BatchSize</term><description>Количество торгов, обрабатываемых за один запуск (по умолчанию: 10).</description></item>
/// </list>
/// </summary>
public class TradeStatusesUpdateBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TradeStatusesUpdateBackgroundService> _logger;
    private readonly IConfiguration _configuration;

    // Конечные статусы торгов
    private static readonly string[] FinalStatuses =
    {
        "Завершенные",
        "Торги отменены",
        "Торги не состоялись"
    };

    public TradeStatusesUpdateBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<TradeStatusesUpdateBackgroundService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Читаем настройку из конфига. По умолчанию (если настройки нет) сервис выключен (false)
        var isEnabled = _configuration.GetValue<bool>("BackgroundServices:TradeStatusesUpdate:Enabled", false);

        if (!isEnabled)
        {
            _logger.LogInformation("TradeStatusesUpdateBackgroundService отключен через конфигурацию.");
            return;
        }

        _logger.LogInformation("TradeStatusesUpdateBackgroundService запущен.");

        // Настройка: нужно ли запустить парсинг немедленно при старте приложения
        var runOnStartup = _configuration.GetValue<bool>("BackgroundServices:TradeStatusesUpdate:RunOnStartup", true);

        if (runOnStartup)
        {
            _logger.LogInformation("Выполняется немедленный запуск парсинга статусов при старте приложения...");
            await ProcessBiddingsAsync(stoppingToken);
        }

        // Настройка часа запуска по расписанию (по умолчанию 2 часа ночи)
        var runAtHour = _configuration.GetValue<int>("BackgroundServices:TradeStatusesUpdate:RunAtHour", 2);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRunTime = now.Date.AddHours(runAtHour);

            // Если сегодня время запуска уже прошло (или мы только что запустили его на старте) — планируем на завтра
            if (now > nextRunTime || (runOnStartup && now.Date == nextRunTime.Date && now.Hour >= runAtHour))
            {
                nextRunTime = nextRunTime.AddDays(1);
            }

            var delay = nextRunTime - now;
            _logger.LogInformation("Следующий запуск парсинга статусов запланирован на {NextRunTime}", nextRunTime);

            try
            {
                // Спим до следующего запуска
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break; // Завершение работы приложения
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            await ProcessBiddingsAsync(stoppingToken);

            // После первого прохода цикла сбрасываем флаг стартапа, чтобы расписание считалось корректно
            runOnStartup = false;
        }
    }

    private async Task ProcessBiddingsAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Считываем размер батча из конфигурации. Если настройки нет, по умолчанию берем 100
            var batchSize = _configuration.GetValue<int>("BackgroundServices:TradeStatusesUpdate:BatchSize", 10);

            // Расширяем массив конечных статусов локально для проверки внутри метода
            var extendedFinalStatuses = FinalStatuses.Concat(new[] { "Торги завершены (нет данных)" }).ToArray();

            _logger.LogInformation("Начинается обработка очереди торгов. Размер батча: {BatchSize}.", batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                // ВАЖНО: Создаем scope на каждый батч. 
                // Это очищает Change Tracker и предотвращает утечку памяти (Memory Leak) при обработке большого количества лотов
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                var scraper = scope.ServiceProvider.GetRequiredService<ITradeCardLotsStatusScraper>();

                // Получаем очередную партию торгов
                var biddingIds = await dbContext.Biddings
                    .Where(b => !b.IsTradeStatusesFinalized &&
                        b.TradePeriod == null &&
                        b.Lots.Any(l => l.LotNumber != null && l.LotNumber != ""))
                    .Select(b => b.Id)
                    .Take(batchSize)
                    .ToListAsync(stoppingToken);

                // Если торгов больше нет — выходим из цикла
                if (biddingIds.Count == 0)
                {
                    _logger.LogInformation("Все доступные торги успешно обработаны. Очередь пуста.");
                    break;
                }

                _logger.LogInformation("Взята партия из {Count} торгов для обновления статусов.", biddingIds.Count);

                // Обрабатываем текущий батч
                foreach (var biddingId in biddingIds)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var bidding = await dbContext.Biddings
                        .Include(b => b.Lots)
                        .FirstOrDefaultAsync(b => b.Id == biddingId, stoppingToken);

                    if (bidding == null) continue;

                    var lotsWithNumbers = bidding.Lots
                        .Where(l => !string.IsNullOrWhiteSpace(l.LotNumber))
                        .ToList();

                    var lotNumbers = lotsWithNumbers.Select(l => l.LotNumber!).Distinct().ToList();

                    if (lotNumbers.Count == 0) continue;

                    // Парсим статусы
                    var statuses = await scraper.ScrapeLotsStatusesAsync(biddingId, lotNumbers, stoppingToken);

                    // Ищем пропущенные лоты (логика нормализации как в контроллере)
                    var missingLots = lotNumbers
                        .Select(n => Regex.Replace(n.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim())
                        .Where(n => !statuses.ContainsKey(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (missingLots.Count > 0)
                    {
                        _logger.LogWarning(
                            "Для торгов {BiddingId} найдены отсутствующие лоты: [{Missing}]. Им будет присвоен технический статус.",
                            biddingId, string.Join(", ", missingLots));
                    }

                    bool allLotsFinalized = true;

                    // Обрабатываем все лоты (и найденные, и missing)
                    foreach (var lot in lotsWithNumbers)
                    {
                        var normalizedLotNumber = Regex.Replace(lot.LotNumber!.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim();

                        if (statuses.TryGetValue(normalizedLotNumber, out var parsedStatus))
                        {
                            // Лот найден — сохраняем нормальные данные
                            lot.TradeStatus = parsedStatus.TradeStatus;
                            lot.FinalPrice = parsedStatus.FinalPrice;
                            lot.WinnerName = parsedStatus.WinnerName;
                            lot.WinnerInn = parsedStatus.WinnerInn;
                        }
                        else
                        {
                            // Лот не найден — проставляем технический статус "заглушку"
                            lot.TradeStatus = "Торги завершены (нет данных)";

                            // Очищаем остальные поля на всякий случай
                            lot.FinalPrice = null;
                            lot.WinnerName = null;
                            lot.WinnerInn = null;
                        }

                        // Проверяем, перешел ли лот в конечный статус (с учетом нашего нового технического статуса)
                        if (string.IsNullOrWhiteSpace(lot.TradeStatus) ||
                            !extendedFinalStatuses.Contains(lot.TradeStatus, StringComparer.OrdinalIgnoreCase))
                        {
                            allLotsFinalized = false;
                        }
                    }

                    // Если по всем лотам этих торгов статусы обновились до конечного
                    if (allLotsFinalized)
                    {
                        bidding.IsTradeStatusesFinalized = true;
                        _logger.LogInformation("Все лоты для торгов {BiddingId} перешли в конечные статусы. Торги закрыты для парсера.", biddingId);
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);

                    // Делаем паузу в 2 секунды между запросами, чтобы старый Федресурс не заблокировал IP
                    await Task.Delay(2000, stoppingToken);
                }

                // На всякий случай добавляем небольшую паузу между батчами
                await Task.Delay(5000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Произошла критическая ошибка при фоновом обновлении статусов торгов.");
        }
    }
}
