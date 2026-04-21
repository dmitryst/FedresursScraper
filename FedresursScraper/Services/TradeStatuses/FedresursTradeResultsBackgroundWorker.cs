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
        var isEnabled = _configuration.GetValue<bool>("Parsing:ResultsScraperEnabled", true);
        if (!isEnabled) return;

        int intervalMinutes = _configuration.GetValue<int>("Parsing:ResultsScraperIntervalMinutes", 60);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var parserService = scope.ServiceProvider.GetRequiredService<FedresursTradeResultsParserService>();
                
                await parserService.ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в цикле фонового парсинга результатов.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}