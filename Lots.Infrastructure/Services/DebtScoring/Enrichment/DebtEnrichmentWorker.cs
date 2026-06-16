using Lots.Application.Services.DebtScoring;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services.DebtScoring.Enrichment;

public class DebtEnrichmentWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DebtScoringOptions> _options;
    private readonly ILogger<DebtEnrichmentWorker> _logger;

    public DebtEnrichmentWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DebtScoringOptions> options,
        ILogger<DebtEnrichmentWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DebtEnrichmentWorker started.");

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
                    var enrichmentService = scope.ServiceProvider.GetRequiredService<IDebtEnrichmentService>();
                    var hasWork = await enrichmentService.ProcessPendingProfilesAsync(stoppingToken);

                    if (hasWork)
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
                    _logger.LogError(ex, "DebtEnrichmentWorker cycle error");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DebtEnrichmentWorker stopped.");
        }
    }
}
