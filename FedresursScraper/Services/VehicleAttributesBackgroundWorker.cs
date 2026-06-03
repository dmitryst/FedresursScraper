using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FedresursScraper.Services;

public class VehicleAttributesBackgroundWorker : BackgroundService
{
    private readonly ILogger<VehicleAttributesBackgroundWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Проверяем каждые 5 минут

    public VehicleAttributesBackgroundWorker(
        ILogger<VehicleAttributesBackgroundWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Фоновый воркер для извлечения атрибутов транспортных средств запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var extractor = scope.ServiceProvider.GetRequiredService<IVehicleAttributesExtractor>();
                    
                    // Вызываем метод извлечения атрибутов
                    // Внутри метода ExtractAttributesForActiveVehiclesAsync уже реализована логика:
                    // - Поиск активных лотов категории "Легковой автомобиль"
                    // - Фильтрация тех, у которых нет ключа "brand"
                    // - Разбивка на батчи по 30 шт и отправка в DeepSeek
                    await extractor.ExtractAttributesForActiveVehiclesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выполнении фоновой задачи извлечения атрибутов транспортных средств.");
            }

            // Ждем перед следующей проверкой
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Фоновый воркер для извлечения атрибутов транспортных средств остановлен.");
    }
}
