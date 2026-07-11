using Microsoft.Extensions.Options;

namespace FedresursScraper.Services;

public class AlfalotEnrichmentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlfalotEnrichmentWorker> _logger;
    private readonly IOptionsMonitor<AlfalotEnrichmentOptions> _options;

    public AlfalotEnrichmentWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AlfalotEnrichmentWorker> logger,
        IOptionsMonitor<AlfalotEnrichmentOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alfalot Enrichment Worker стартовал.");

        try
        {
            // Не стартуем вместе с каталогом
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_options.CurrentValue.IsEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var enrichmentService = scope.ServiceProvider.GetRequiredService<IAlfalotEnrichmentService>();

                    bool hasWork = await enrichmentService.ProcessPendingBiddingsAsync(stoppingToken);

                    if (hasWork)
                    {
                        var delaySeconds = _options.CurrentValue.GetEnrichmentBatchDelaySeconds();
                        _logger.LogDebug("Пауза между пачками enrichment Альфалот: {Seconds} сек", delaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    }
                    else
                    {
                        var delayMinutes = _options.CurrentValue.DelayWhenNoWorkMinutes > 0
                            ? _options.CurrentValue.DelayWhenNoWorkMinutes
                            : 10;
                        await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Критическая ошибка в цикле Alfalot Enrichment Worker");
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Alfalot Enrichment Worker остановлен.");
        }
    }
}
