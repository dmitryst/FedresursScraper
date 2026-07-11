using Microsoft.Extensions.Options;

namespace FedresursScraper.Services;

public class AlfalotCatalogIndexerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlfalotCatalogIndexerWorker> _logger;
    private readonly IOptionsMonitor<AlfalotEnrichmentOptions> _options;

    public AlfalotCatalogIndexerWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AlfalotCatalogIndexerWorker> logger,
        IOptionsMonitor<AlfalotEnrichmentOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alfalot Catalog Indexer Worker стартовал.");

        try
        {
            var startupDelay = Math.Max(10, _options.CurrentValue.CatalogStartupDelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(startupDelay), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var options = _options.CurrentValue;
                if (!options.CatalogIndexerEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var indexer = scope.ServiceProvider.GetRequiredService<IAlfalotCatalogIndexerService>();
                    await indexer.IndexCatalogAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка цикла индексации каталога Альфалот");
                }

                var interval = options.CatalogIndexerIntervalMinutes > 0
                    ? options.CatalogIndexerIntervalMinutes
                    : 120;
                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Alfalot Catalog Indexer Worker остановлен.");
        }
    }
}
