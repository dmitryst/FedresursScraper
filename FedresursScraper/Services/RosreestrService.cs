using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

// Модели для десериализации ответа от rosreestr2coord
// Нам не нужны все поля, только те, что ведут к координатам
public class GeoJsonFeature
{
    [System.Text.Json.Serialization.JsonPropertyName("geometry")]
    public Geometry? Geometry { get; set; }
}

public class Geometry
{
    [System.Text.Json.Serialization.JsonPropertyName("coordinates")]
    public List<List<List<double>>>? Coordinates { get; set; }
}

public interface IRosreestrService
{
    Task<double[]?> FindFirstCoordinatesAsync(IEnumerable<string?>? cadastralNumbers);
}

public class RosreestrService : IRosreestrService
{
    private readonly ILogger<RosreestrService> _logger;
    // Укажите путь к python.exe, если его нет в системной переменной PATH
    private readonly string _pythonExecutablePath = "python";

    public RosreestrService(ILogger<RosreestrService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Находит первую точку координат для первого валидного кадастрового номера.
    /// </summary>
    /// <param name="cadastralNumbers">Список кадастровых номеров.</param>
    /// <returns>Массив double[2] с координатами [долгота, широта] или null.</returns>
    public async Task<double[]?> FindFirstCoordinatesAsync(IEnumerable<string?>? cadastralNumbers)
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

            string? geoJson = await GetGeoJsonFromFile(number);

            if (string.IsNullOrWhiteSpace(geoJson))
            {
                continue;
            }

            try
            {
                var feature = JsonSerializer.Deserialize<GeoJsonFeature>(geoJson);

                // Извлекаем первую точку из первого полигона: [[[point1], [point2], ...]]
                var firstPoint = feature?.Geometry?.Coordinates?
                                        .FirstOrDefault()?
                                        .FirstOrDefault();

                if (firstPoint != null && firstPoint.Count >= 2)
                {
                    _logger.LogInformation("Успешно найдены координаты для номера {CadastralNumber}.", number);
                    return firstPoint.ToArray();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Ошибка десериализации JSON для кадастрового номера {CadastralNumber}", number);
            }
        }

        _logger.LogWarning("Координаты не были найдены ни для одного из предоставленных кадастровых номеров.");
        return null;
    }

    /// <summary>
    /// Вызывает python-скрипт, который сохраняет результат в файл, читает его и затем удаляет.
    /// </summary>
    private async Task<string?> GetGeoJsonFromFile(string cadastralNumber)
    {
        // Путь, который монтируется через emptyDir в deployment
        string dir = "/app/temp_rosreestr";

        Directory.CreateDirectory(dir);

        // Имя файла, которое создаст rosreestr2coord
        string outputFileName = $"{cadastralNumber.Replace(':', '_')}.geojson";
        string fullPath = Path.Combine(dir, outputFileName);

        var arguments = $"-m rosreestr2coord -c {cadastralNumber}";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = new Process { StartInfo = processStartInfo };

            _logger.LogInformation("Запуск Python-скрипта для {CadastralNumber}. Аргументы: {Arguments}", cadastralNumber, arguments);
            process.Start();

            // Считываем вывод для отладки, хотя основной результат ожидаем в файле
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Python-скрипт завершился с ошибкой для номера {CadastralNumber}: {Error}", cadastralNumber, error);
                return null;
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogError("Python-скрипт выполнен успешно, но файл результата {FullPath} не найден.", fullPath);
                return null;
            }

            _logger.LogInformation("Файл {FullPath} успешно создан, читаем содержимое.", fullPath);
            return await File.ReadAllTextAsync(fullPath);
        }
        finally
        {
            // Очистка: удаляем файл, если он был создан
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("Временный файл {FullPath} удален.", fullPath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить временный файл {FullPath}.", fullPath);
                }
            }
        }
    }
}
