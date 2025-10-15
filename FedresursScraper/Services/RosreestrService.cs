using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

#region Coordinate Conversion
/// <summary>
/// Утилитарный класс для преобразования координат.
/// </summary>
public static class CoordinateConverter
{
    // Радиус Земли в метрах
    private const double EarthRadius = 6378137.0;

    /// <summary>
    /// Преобразует координаты из Web Mercator (EPSG:3857) в WGS 84 (EPSG:4326).
    /// </summary>
    /// <param name="x">Координата X в метрах (смещение на восток).</param>
    /// <param name="y">Координата Y в метрах (смещение на север).</param>
    /// <returns>Кортеж с долготой (Longitude) и широтой (Latitude) в градусах.</returns>
    public static (double Longitude, double Latitude) WebMercatorToWgs84(double x, double y)
    {
        double lon = (x / EarthRadius) * 180.0 / Math.PI;
        double lat = (2.0 * Math.Atan(Math.Exp(y / EarthRadius)) - Math.PI / 2.0) * 180.0 / Math.PI;
        return (lon, lat);
    }
}
#endregion

#region GeoJSON Models
// Модели для десериализации ответа от rosreestr2coord
// Используем JsonElement для гибкой обработки разных структур координат

public class GeoJsonFeature
{
    [JsonPropertyName("geometry")]
    public Geometry? Geometry { get; set; }
}

public class Geometry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("coordinates")]
    public JsonElement Coordinates { get; set; }

    [JsonPropertyName("crs")]
    public Crs? Crs { get; set; }
}

public class Crs
{
    [JsonPropertyName("properties")]
    public CrsProperties? Properties { get; set; }
}

public class CrsProperties
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
#endregion

public interface IRosreestrService
{
    /// <summary>
    /// Находит первую точку координат для первого валидного кадастрового номера.
    /// </summary>
    /// <param name="cadastralNumbers">Список кадастровых номеров.</param>
    /// <returns>Кортеж (Latitude, Longitude) или null, если координаты не найдены.</returns>
    Task<(double Latitude, double Longitude)?> FindFirstCoordinatesAsync(IEnumerable<string>? cadastralNumbers);
}

public class RosreestrService : IRosreestrService
{
    private readonly ILogger<RosreestrService> _logger;
    // Указываем путь к python.exe, если его нет в системной переменной PATH
    private readonly string _pythonExecutablePath;
    private readonly string _tempDirPath;

    // Опции для десериализации JSON без учета регистра
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RosreestrService(ILogger<RosreestrService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _pythonExecutablePath = configuration["Python:ExecutablePath"] ?? "python";
        _tempDirPath = configuration["Paths:TempRosreestrDir"] ?? "/app/temp_rosreestr";

        if (!Directory.Exists(_tempDirPath))
        {
            Directory.CreateDirectory(_tempDirPath);
        }
    }

    public async Task<(double Latitude, double Longitude)?> FindFirstCoordinatesAsync(IEnumerable<string>? cadastralNumbers)
    {
        if (cadastralNumbers == null) return null;

        foreach (var number in cadastralNumbers)
        {
            if (string.IsNullOrWhiteSpace(number)) continue;

            string? geoJsonContent = await GetGeoJsonFromScript(number);
            if (string.IsNullOrWhiteSpace(geoJsonContent)) continue;

            try
            {
                var feature = JsonSerializer.Deserialize<GeoJsonFeature>(geoJsonContent, _jsonOptions);
                var geometry = feature?.Geometry;

                if (geometry == null || geometry.Coordinates.ValueKind == JsonValueKind.Undefined)
                {
                    _logger.LogWarning("Геометрия или координаты отсутствуют для номера {CadastralNumber}", number);
                    continue;
                }

                // Извлекаем первую точку, независимо от типа геометрии (Point, Polygon, etc.)
                var firstPointCoords = GetFirstPoint(geometry);

                if (firstPointCoords == null || firstPointCoords.Count < 2)
                {
                    _logger.LogWarning("Не удалось извлечь координаты из геометрии для {CadastralNumber}", number);
                    continue;
                }

                double coord1 = firstPointCoords[0];
                double coord2 = firstPointCoords[1];

                bool isPolygonOrMultiPolygon = string.Equals(geometry.Type, "Polygon", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(geometry.Type, "MultiPolygon", StringComparison.OrdinalIgnoreCase);
                if (isPolygonOrMultiPolygon)
                {
                    // Для полигонов, даже если CRS указан как EPSG:3857, координаты фактически
                    // предоставляются в градусах (WGS84). Поэтому конвертация не нужна.
                    // Нужно только поменять местами порядок [lon, lat] на [lat, lon] для API карт.
                    _logger.LogInformation("Обработка Polygon для {CadastralNumber}. Координаты считаются WGS84. Конвертация не требуется.", number);
                    return (coord2, coord1); // Возвращаем как (Latitude, Longitude)
                }
                
                // Для всех остальных типов (включая Point) используем стандартную логику: проверяем CRS.
                bool needsConversion = geometry?.Crs?.Properties?.Name?.Contains("EPSG:3857") ?? false;
                if (needsConversion)
                {
                    var (lon, lat) = CoordinateConverter.WebMercatorToWgs84(coord1, coord2);
                    _logger.LogInformation("Координаты для {CadastralNumber} были конвертированы из EPSG:3857.", number);
                    return (lat, lon); // Возвращаем в порядке [lat, lon]
                }

                // Если конвертация не нужна, считаем, что это WGS84 [lon, lat]
                return (coord2, coord1); // Возвращаем, поменяв местами [lat, lon]
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
    /// Рекурсивно извлекает первую пару координат из JsonElement.
    /// </summary>
    private List<double>? GetFirstPoint(Geometry geometry)
    {
        JsonElement currentElement = geometry.Coordinates;

        // Погружаемся вглубь массива, пока не дойдем до элемента с числами
        while (currentElement.ValueKind == JsonValueKind.Array &&
               currentElement.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.Array)
        {
            currentElement = currentElement.EnumerateArray().FirstOrDefault();
        }

        if (currentElement.ValueKind == JsonValueKind.Array)
        {
            return currentElement.EnumerateArray()
                                 .Where(e => e.ValueKind == JsonValueKind.Number)
                                 .Select(e => e.GetDouble())
                                 .ToList();
        }

        return null;
    }


    /// <summary>
    /// Вызывает python-скрипт, который сохраняет результат в файл, читает его и затем удаляет
    /// </summary>
    /// <param name="cadastralNumber">Кадастровый номер участка</param>
    /// <returns>Содержимое файла с данными о земельном участке</returns>
    private async Task<string?> GetGeoJsonFromScript(string cadastralNumber)
    {
        // rosreestr2coord сохраняет в <_tempDirPath>/output/geojson
        string dir = Path.Combine(_tempDirPath, "output", "geojson");
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
            WorkingDirectory = _tempDirPath,
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
                // несмотря на то, что скрипт завершился с ошибкой, файл должен быть создан
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogError("Python-скрипт выполнен успешно, но файл результата {FullPath} не найден.", fullPath);
                return null;
            }

            _logger.LogInformation("Файл {FullPath} успешно создан, читаем содержимое.", fullPath);
            return await File.ReadAllTextAsync(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Произошла ошибка при выполнении python-скрипта для номера {CadastralNumber}", cadastralNumber);
            return null;
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