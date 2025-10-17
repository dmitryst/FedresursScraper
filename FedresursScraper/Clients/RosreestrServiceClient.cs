// RosreestrServiceClient.cs

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
                // Отправляем GET-запрос и ожидаем JSON-массив double[]
                var coordinatesArray = await _httpClient.GetFromJsonAsync<double[]>(requestUrl);

                if (coordinatesArray != null && coordinatesArray.Length == 2)
                {
                    // Python-сервис возвращает [latitude, longitude]
                    _logger.LogInformation("Координаты для кадастрового номера {CadastralNumber} успешно получены.", number);
                    return new Coordinates { Latitude = coordinatesArray[0], Longitude = coordinatesArray[1] };
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Это ожидаемое поведение, если для номера нет координат (сервис вернет 404)
                _logger.LogInformation("Координаты для кадастрового номера {CadastralNumber} не найдены (404).", number);
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
}
