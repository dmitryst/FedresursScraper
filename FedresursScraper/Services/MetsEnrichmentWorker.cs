using FedresursScraper.Services;
using Microsoft.Extensions.Options;

public class MetsEnrichmentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetsEnrichmentWorker> _logger;
    private readonly IOptionsMonitor<MetsEnrichmentOptions> _options;

    public MetsEnrichmentWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MetsEnrichmentWorker> logger,
        IOptionsMonitor<MetsEnrichmentOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mets Enrichment Worker стартовал.");
        _logger.LogInformation("Config: IsEnabled={Enabled}, Delay={Delay}Min",
            _options.CurrentValue.IsEnabled,
            _options.CurrentValue.DelayWhenNoWorkMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            // проверка флага отключения
            if (!_options.CurrentValue.IsEnabled)
            {
                _logger.LogWarning("Mets Enrichment Worker ОТКЛЮЧЕН (IsEnabled=false). Жду 1 мин...");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var enrichmentService = scope.ServiceProvider.GetRequiredService<IMetsEnrichmentService>();

                    _logger.LogInformation("Вызов метода ProcessPendingBiddingsAsync...");

                    bool hasWork = await enrichmentService.ProcessPendingBiddingsAsync(stoppingToken);
                    // дебаг
                    // hasWork = false; 

                    if (hasWork)
                    {
                        // Генерируем случайную задержку от 10 до 15 секунд
                        int delaySeconds = Random.Shared.Next(10, 16);
                        var randomDelay = TimeSpan.FromSeconds(delaySeconds);

                        _logger.LogDebug("Пачка обработана. Ожидание {Seconds} сек...", delaySeconds);

                        // Если работа была, делаем короткую паузу и продолжаем молотить
                        await Task.Delay(randomDelay, stoppingToken);
                    }
                    else
                    {
                        // Если работы нет (все спарсили), спим дольше
                        _logger.LogInformation("Нет лотов для обработки. Переход в режим ожидания...");

                        var delayMinutes = _options.CurrentValue.DelayWhenNoWorkMinutes > 0
                            ? _options.CurrentValue.DelayWhenNoWorkMinutes
                            : 5;
                        await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле Mets Enrichment Worker");
                // Пауза при ошибке, чтобы не дудосить БД в цикле при отвале сети
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
