using FedresursScraper.Services;

public class MetsEnrichmentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetsEnrichmentWorker> _logger;
    
    // Настройки задержки
    private readonly TimeSpan _delayWhenNoWork = TimeSpan.FromMinutes(5);

    public MetsEnrichmentWorker(IServiceScopeFactory scopeFactory, ILogger<MetsEnrichmentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mets Enrichment Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var enrichmentService = scope.ServiceProvider.GetRequiredService<IMetsEnrichmentService>();
                    
                    bool hasWork = await enrichmentService.ProcessPendingBiddingsAsync(stoppingToken);
                    hasWork = false;  // для дебага

                    if (hasWork)
                    {
                        // Генерируем случайную задержку от 10 до 15 секунд
                        int delaySeconds = Random.Shared.Next(10, 16);
                        var randomDelay = TimeSpan.FromSeconds(delaySeconds);

                        _logger.LogDebug("Batch processed. Sleeping for {Seconds} seconds...", delaySeconds);

                        // Если работа была, делаем короткую паузу и продолжаем молотить
                        await Task.Delay(randomDelay, stoppingToken);
                    }
                    else
                    {
                        // Если работы нет (все спарсили), спим дольше
                        _logger.LogInformation("No pending lots. Sleeping...");
                        await Task.Delay(_delayWhenNoWork, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in Mets Worker loop");
                // Пауза при ошибке, чтобы не дудосить БД в цикле при отвале сети
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
