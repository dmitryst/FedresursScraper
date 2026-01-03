using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Разгребает очередь задач на классификацию лотов.
/// </summary>
public class LotClassificationService : BackgroundService
{
    private readonly ILogger<LotClassificationService> _logger;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;

    public LotClassificationService(
        IBackgroundTaskQueue taskQueue,
        ILogger<LotClassificationService> logger,
        IServiceProvider serviceProvider)
    {
        _taskQueue = taskQueue;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Lot Classification Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                if (workItem != null)
                {
                    await workItem(_serviceProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Предотвращаем логирование исключения при штатной остановке сервиса
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing work item.");
            }
        }

        _logger.LogInformation("Lot Classification Service is stopping.");
    }
}
