using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

public class RosreestrWorker : BackgroundService
{
    private readonly IRosreestrQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RosreestrWorker> _logger;
    private readonly IConfiguration _configuration;

    public RosreestrWorker(
        IRosreestrQueue queue, 
        IServiceProvider serviceProvider, 
        ILogger<RosreestrWorker> logger,
        IConfiguration configuration)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int delaySeconds = _configuration.GetValue<int>("Rosreestr:RequestDelaySeconds", 20);

        _logger.LogInformation("RosreestrWorker запущен. Задержка между запросами: {Delay} сек.", delaySeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);

                using (var scope = _serviceProvider.CreateScope())
                {
                    try 
                    {
                        await workItem(scope.ServiceProvider, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при выполнении задачи обновления координат.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в RosreestrWorker.");
            }
        }
    }
}
