using FedresursScraper.Services.Utils;

namespace FedresursScraper.Tests;

public class GeoJsonUtilsTests
{
    [Fact]
    public void ExtractPointFromGeoJson_ReturnsLatLon_InExpectedRange_ForKnownSamples()
    {
        // Участок 47:29:1025006:2 — Ленинградская область
        // Ожидаем: lat ≈ 59.182, lon ≈ 29.992
        var geoJson47 = File.ReadAllText("47_29_1025006_2.geojson");
        var point47 = GeoJsonUtils.ExtractPointFromGeoJson(geoJson47);
        Assert.NotNull(point47);
        Assert.InRange(point47.Value.Lat, 59.0, 60.0); // Ленобласть
        Assert.InRange(point47.Value.Lon, 28.0, 31.0);

        // Участок 63:02:0309002:518 — Самарская область (Жигулевск)
        // Ожидаем: lat ≈ 53.390, lon ≈ 49.512
        var geoJson63 = File.ReadAllText("63_02_0309002_518.geojson");
        var point63 = GeoJsonUtils.ExtractPointFromGeoJson(geoJson63);
        Assert.NotNull(point63);
        Assert.InRange(point63.Value.Lat, 53.0, 54.0); // Самарская область
        Assert.InRange(point63.Value.Lon, 49.0, 50.0);
    }
}
