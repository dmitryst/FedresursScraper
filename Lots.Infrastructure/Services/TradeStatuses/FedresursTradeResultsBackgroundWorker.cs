namespace FedresursScraper.Services;

public class FedresursTradeResultsBackgroundWorker : BackgroundService
{
    private readonly ILogger<FedresursTradeResultsBackgroundWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public FedresursTradeResultsBackgroundWorker(
        ILogger<FedresursTradeResultsBackgroundWorker> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var isEnabled = _configuration.GetValue<bool>("BackgroundServices:FedresursTradeResults:Enabled", false);

        if (!isEnabled)
        {
            _logger.LogInformation("FedresursTradeResultsBackgroundWorker отключен через конфигурацию.");
            return;
        }

        _logger.LogInformation("FedresursTradeResultsBackgroundWorker запущен.");

        int intervalMinutes = _configuration.GetValue<int>("BackgroundServices:FedresursTradeResults:IntervalMinutesBetweenBatches", 1);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var parserService = scope.ServiceProvider.GetRequiredService<FedresursTradeResultsParserService>();
                    
                    await parserService.ProcessBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в цикле фонового парсинга результатов.");
                }

                _logger.LogInformation($"Ожидаем {intervalMinutes} минут до начала парсинга следующего батча результатов торгов");
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("FedresursTradeResultsBackgroundWorker остановлен (OperationCanceledException).");
        }
    }
}