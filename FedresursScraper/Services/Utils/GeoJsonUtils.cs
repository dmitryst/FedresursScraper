using System.Text.Json;

namespace FedresursScraper.Services.Utils;

public static class GeoJsonUtils
{
    /// <summary>
    /// Рекурсивно извлекает первую пару координат из coordinates любой вложенности.
    /// Работает с Point, LineString, Polygon, MultiPolygon.
    /// GeoJSON всегда хранит в порядке [longitude, latitude].
    /// </summary>
    public static (double Lon, double Lat)? GetFirstPoint(JsonElement coordinates)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
            return null;

        var items = coordinates.EnumerateArray().ToList();
        if (items.Count == 0)
            return null;

        // Это конечная пара координат: первый элемент — число (lon)
        if (items[0].ValueKind == JsonValueKind.Number && items.Count >= 2)
        {
            var lon = items[0].GetDouble();
            var lat = items[1].GetDouble();
            return (lon, lat);
        }

        // Это вложенный массив — рекурсия в первый элемент
        return GetFirstPoint(items[0]);
    }

    /// <summary>
    /// Извлекает первую точку (Lat, Lon) из сырого GeoJSON.
    /// Автоматически определяет, нужна ли конвертация из Web Mercator (EPSG:3857) в WGS84.
    ///
    /// Важно: rosreestr2coord 5.x при работе с новым НСПД API возвращает координаты
    /// уже в WGS84 (градусы), но оставляет тег CRS = "EPSG:3857".
    /// Поэтому мы используем проверку диапазона значений, а не только тег CRS.
    /// </summary>
    public static (double Lat, double Lon)? ExtractPointFromGeoJson(string? rawGeoJson)
    {
        if (string.IsNullOrWhiteSpace(rawGeoJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rawGeoJson);
            var root = document.RootElement;

            // Пробуем достать geometry как на уровне Feature, так и напрямую
            var geometry = root.TryGetProperty("geometry", out var geom) ? geom : root;

            if (!geometry.TryGetProperty("coordinates", out var coordinates))
                return null;

            var point = GetFirstPoint(coordinates);
            if (point == null)
                return null;

            var (lon, lat) = point.Value;

            // Определяем систему координат:
            // Если значения выходят за пределы WGS84 (|lon| > 180 или |lat| > 90),
            // это точно метры Web Mercator — конвертируем.
            // Если тег CRS = EPSG:3857, но значения в пределах WGS84 — библиотека уже
            // сконвертировала (поведение rosreestr2coord 5.x), оставляем как есть.
            bool isMercator = Math.Abs(lon) > 180 || Math.Abs(lat) > 90;

            if (isMercator)
            {
                (lon, lat) = WebMercatorToWgs84(lon, lat);
            }

            return (Lat: lat, Lon: lon);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Конвертирует координаты из Web Mercator (EPSG:3857, единицы — метры)
    /// в WGS84 (EPSG:4326, единицы — градусы).
    /// Эквивалент функции webmercator_to_wgs84() из app.py.
    /// </summary>
    public static (double Lon, double Lat) WebMercatorToWgs84(double x, double y)
    {
        const double earthRadius = 6378137.0;
        var lon = x / earthRadius * 180.0 / Math.PI;
        var lat = (2.0 * Math.Atan(Math.Exp(y / earthRadius)) - Math.PI / 2.0) * 180.0 / Math.PI;
        return (lon, lat);
    }
}
