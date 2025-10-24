using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

// Модель для хранения координат
public class Coordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public interface IRosreestrServiceClient
{
    /// <summary>
    /// Находит первую точку координат для первого валидного кадастрового номера.
    /// </summary>
    /// <param name="cadastralNumbers">Список кадастровых номеров.</param>
    /// <returns>Координаты участка</returns>
    Task<Coordinates?> FindFirstCoordinatesAsync(IEnumerable<string> cadastralNumbers);
}

public class RosreestrServiceClient : IRosreestrServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RosreestrServiceClient> _logger;
    private static readonly ConcurrentQueue<string> _retryQueue = new();


    public RosreestrServiceClient(HttpClient httpClient, ILogger<RosreestrServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Coordinates?> FindFirstCoordinatesAsync(IEnumerable<string> cadastralNumbers)
    {
        if (cadastralNumbers == null)
        {
            return null;
        }

        foreach (var number in cadastralNumbers)
        {
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var requestUrl = $"coordinates/{Uri.EscapeDataString(number)}";

            try
            {
                // Вызываем rosreestr-service
                var coordinatesArray = await GetCoordinatesWithRetryAsync(requestUrl, number);

                if (coordinatesArray != null && coordinatesArray.Length == 2)
                {
                    // rosreestr-service возвращает [latitude, longitude]
                    _logger.LogInformation("Координаты для кадастрового номера {CadastralNumber} успешно получены.", number);
                    return new Coordinates { Latitude = coordinatesArray[0], Longitude = coordinatesArray[1] };
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Это ожидаемое поведение, если нет такого КН или для КН нет координат (сервис вернет 404)
                _logger.LogInformation("Координаты для кадастрового номера {CadastralNumber} не найдены (404).", number);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // 403 - не retry, а логируем и возвращаем null
                _logger.LogWarning("Доступ запрещён для кадастрового номера {CadastralNumber} (403 Forbidden)", number);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вызове сервиса rosreestr-service для номера {CadastralNumber}", number);
                // В случае другой ошибки (например, сервис недоступен) переходим к следующему номеру
            }
        }

        // Если ни для одного номера не удалось найти координаты
        return null;
    }

    private async Task<double[]?> GetCoordinatesWithRetryAsync(string requestUrl, string cadastralNumber)
    {
        // Политика Retry: повторяем запрос при 500-й ошибке или других временных сбоях сети
        var retryPolicy = Policy
            .Handle<HttpRequestException>(r => r.StatusCode == HttpStatusCode.InternalServerError || r.StatusCode == null)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // Экспоненциальная задержка: 2, 4, 8 секунд
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning($"Попытка {retryCount} по кадастровому номеру {cadastralNumber} завершилась неудачей. Повтор через {timespan}.");
                });

        // Политика Fallback: если все попытки retry не увенчались успехом, добавляем номер в очередь
        var fallbackPolicy = Policy<double[]?>
            .Handle<HttpRequestException>(r => r.StatusCode == HttpStatusCode.InternalServerError)
            .FallbackAsync(
                fallbackValue: null,
                onFallbackAsync: async (outcome, context) =>
                {
                    await AddToRetryQueueAsync(cadastralNumber);
                    _logger.LogError("Кадастровый номер {CadastralNumber} добавлен в очередь на повторную обработку после неудачных попыток", cadastralNumber);
                });

        // Оборачиваем одну политику в другую. Порядок важен: сначала fallback, потом retry.
        var policyWrap = fallbackPolicy.WrapAsync(retryPolicy);

        // Выполняем весь цикл запроса-ответа внутри ExecuteAsync
        return await policyWrap.ExecuteAsync(async () =>
        {
            var response = await _httpClient.GetAsync(requestUrl);

            // Если пришел неуспешный статус, генерируем исключение, чтобы Polly мог его обработать
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Ошибка от сервиса координат. Статус: {response.StatusCode}", null, response.StatusCode);
            }

            // Если статус успешный, десериализуем и возвращаем результат
            return await response.Content.ReadFromJsonAsync<double[]>();
        });
    }

    private Task AddToRetryQueueAsync(string cadastralNumber)
    {
        _retryQueue.Enqueue(cadastralNumber);
        _logger.LogInformation("Кадастровый номер {Number} добавлен в очередь для повторной обработки.", cadastralNumber);
        return Task.CompletedTask;
    }
}
