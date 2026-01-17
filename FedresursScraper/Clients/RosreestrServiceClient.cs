using System.Net;
using Polly;
using Polly.Retry;

public interface IRosreestrServiceClient
{
    /// <summary>
    /// Выполняет GET-запрос к API для получения координат.
    /// <para>
    /// При ошибках 5xx или проблемах сети выполняет 3 повторные попытки с экспоненциальной задержкой.
    /// Если все попытки неудачны, выбрасывает исключение для обработки на уровне сервиса.
    /// </para>
    /// </summary>
    /// <param name="cadastralNumber">Кадастровый номер.</param>
    /// <returns>Массив [lat, lon] или <c>null</c> при статусах 404/403.</returns>
    Task<double[]?> GetCoordinatesAsync(string cadastralNumber);
}

/// <summary>
/// HTTP-клиент для взаимодействия с API rosreestr-service.
/// Реализует политику повторных попыток (Retry Policy) для временных сетевых ошибок (5xx, Timeouts).
/// </summary>
public class RosreestrServiceClient : IRosreestrServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RosreestrServiceClient> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;


    public RosreestrServiceClient(
        HttpClient httpClient,
        ILogger<RosreestrServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.InternalServerError)
            .Or<HttpRequestException>() // Ловим сетевые ошибки
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2, 4, 8 сек
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Попытка {Retry} по кадастровому номеру {Number} завершилась неудачей. Статус: {Status}. Повтор через {Delay}.",
                        retryCount,
                        context.TryGetValue("CadastralNumber", out var n) ? n : "N/A",
                        outcome.Result?.StatusCode,
                        timespan);
                });
    }

    public async Task<double[]?> GetCoordinatesAsync(string cadastralNumber)
    {
        var requestUrl = $"coordinates/{Uri.EscapeDataString(cadastralNumber)}";

        var context = new Context { ["CadastralNumber"] = cadastralNumber };

        var response = await _retryPolicy.ExecuteAsync(async (ctx) =>
         {
             return await _httpClient.GetAsync(requestUrl);
         }, context);

        // 404 - просто лог и null
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Координаты для кадастрового номера {CadastralNumber} не найдены (404).",
                cadastralNumber);
            return null;
        }

        // 403 – лог и null, без ретраев и без очереди
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "Доступ запрещён для кадастрового номера {CadastralNumber} (403 Forbidden).",
                cadastralNumber);
            return null;
        }

        // Если это 5xx, сюда мы не дойдём: retryPolicy после 3 попыток выбросит HttpRequestException.
        // Для прочих неуспешных статусов пусть тоже падает исключение.
        response.EnsureSuccessStatusCode();

        // rosreestr-service возвращает [latitude, longitude]
        return await response.Content.ReadFromJsonAsync<double[]>();
    }
}
