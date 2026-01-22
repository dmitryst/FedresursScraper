using FedresursScraper.Services;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Services
{
    public class CdtEnrichmentWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CdtEnrichmentWorker> _logger;
        private readonly IOptionsMonitor<MetsEnrichmentOptions> _options; // Пока используем те же настройки что и для МЭТС

        public CdtEnrichmentWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<CdtEnrichmentWorker> logger,
            IOptionsMonitor<MetsEnrichmentOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CDT Enrichment Worker стартовал.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_options.CurrentValue.IsEnabled)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var enrichmentService = scope.ServiceProvider.GetRequiredService<ICdtEnrichmentService>();

                        bool hasWork = await enrichmentService.ProcessPendingBiddingsAsync(stoppingToken);

                        if (hasWork)
                        {
                            int delaySeconds = Random.Shared.Next(10, 16);
                            var randomDelay = TimeSpan.FromSeconds(delaySeconds);

                            _logger.LogDebug("Пачка обработана. Ожидание {Seconds} сек...", delaySeconds);

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
                    _logger.LogError(ex, "Критическая ошибка в цикле CDT Enrichment Worker");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
    }
}
