using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lots.Data.Entities;
using FedresursScraper.Integrations.Fedresurs.Clients;
using FedresursScraper.Integrations.Fedresurs.Models;
using Microsoft.Extensions.Options;

namespace FedresursScraper.Integrations.Fedresurs.Workers;

public class FedresursAggregatorService : BackgroundService
{
    private readonly ILogger<FedresursAggregatorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IFedresursApiClient _apiClient;
    private readonly FedresursWorkerOptions _options;
    private readonly TimeSpan _pollingInterval;

    public FedresursAggregatorService(
        ILogger<FedresursAggregatorService> logger,
        IServiceProvider serviceProvider,
        IFedresursApiClient apiClient,
        IOptions<FedresursWorkerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _apiClient = apiClient;
        _options = options.Value;

        _pollingInterval = TimeSpan.FromSeconds(_options.AggregatorIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Агрегатор сообщений Федресурса запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchNewMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в цикле агрегации данных.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }
    }

    private async Task FetchNewMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        // Определяем окно времени для запроса
        // Ищем дату публикации самого свежего сообщения в базе
        var lastPublishedMessage = await dbContext.RawFedresursMessages
            .OrderByDescending(m => m.DatePublish)
            .FirstOrDefaultAsync(stoppingToken);

        // Если база пустая, берем данные за X часов назад, как указано в конфиге
        DateTime dateBegin = lastPublishedMessage?.DatePublish
            ?? DateTime.UtcNow.AddHours(-_options.InitialFetchHoursBack);

        DateTime dateEnd = DateTime.UtcNow;

        // API не разрешает разницу больше 31 дня, страхуемся
        if ((dateEnd - dateBegin).TotalDays > 31)
        {
            dateEnd = dateBegin.AddDays(31);
        }

        _logger.LogInformation($"Запрашиваем сообщения с {dateBegin:O} по {dateEnd:O}");

        int offset = 0;
        const int limit = 500;
        bool hasMoreData = true;

        while (hasMoreData && !stoppingToken.IsCancellationRequested)
        {
            var response = await _apiClient.GetMessagesAsync(
                dateBegin, dateEnd, _options.TargetTypes, offset, _options.ApiRequestLimit, stoppingToken);

            // На всякий случай ставим задержку
            await Task.Delay(1000);

            if (response == null || response.PageData == null || !response.PageData.Any())
            {
                break; // Данных больше нет
            }

            var newRawMessages = new List<RawFedresursMessage>();

            foreach (var msg in response.PageData)
            {
                // Проверяем на дубликаты (Idempotency)
                bool exists = await dbContext.RawFedresursMessages.AnyAsync(r => r.Guid == msg.Guid, stoppingToken);
                if (exists) continue;

                newRawMessages.Add(new RawFedresursMessage
                {
                    Guid = msg.Guid,
                    Number = msg.Number,
                    Type = msg.Type,
                    DatePublish = DateTime.SpecifyKind(msg.DatePublish, DateTimeKind.Utc),
                    Content = msg.Content,
                    IsLocked = !string.IsNullOrEmpty(msg.LockReason)
                });
            }

            if (newRawMessages.Any())
            {
                dbContext.RawFedresursMessages.AddRange(newRawMessages);
                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation($"Сохранено {newRawMessages.Count} новых сырых сообщений.");
            }

            offset += limit;
            hasMoreData = offset < response.Total;
        }
    }
}
