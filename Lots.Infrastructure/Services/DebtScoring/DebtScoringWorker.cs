using Lots.Application.Services.DebtScoring;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services.DebtScoring;

public class DebtScoringWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DebtScoringOptions> _options;
    private readonly ILogger<DebtScoringWorker> _logger;

    public DebtScoringWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DebtScoringOptions> options,
        ILogger<DebtScoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DebtScoringWorker started.");

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
                    var discovery = scope.ServiceProvider.GetRequiredService<IDebtLotDiscoveryService>();
                    var processing = scope.ServiceProvider.GetRequiredService<IDebtDocumentProcessingService>();

                    var discovered = await discovery.DiscoverPendingLotsAsync(stoppingToken);
                    var processed = await processing.ProcessPendingProfilesAsync(stoppingToken);

                    if (discovered || processed)
                    {
                        var delaySeconds = _options.CurrentValue.DelayBetweenBatchesSeconds;
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                    }
                    else
                    {
                        var delayMinutes = _options.CurrentValue.DelayWhenNoWorkMinutes;
                        await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DebtScoringWorker cycle error");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DebtScoringWorker stopped.");
        }
    }
}
