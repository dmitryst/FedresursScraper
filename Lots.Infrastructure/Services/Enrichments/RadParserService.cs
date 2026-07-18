using FedresursScraper.Services.Enrichments;

namespace FedresursScraper.Services;

/// <summary>
/// Низкоуровневый доступ к каталогу РАД (для probe-эндпоинтов).
/// Основной пайплайн — <see cref="RadCatalogIndexerService"/> / <see cref="RadEnrichmentService"/>.
/// </summary>
public class RadParserService
{
    private readonly HttpClient _httpClient;

    public RadParserService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Парсит список ссылок на лоты с текущей страницы каталога.
    /// </summary>
    public async Task<List<string>> GetLotUrlsFromCatalogAsync(string catalogUrl, CancellationToken ct = default)
    {
        var absoluteUrl = catalogUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? catalogUrl
            : RadHtmlParser.BaseUrl.TrimEnd('/') + "/" + catalogUrl.TrimStart('/');

        var html = await _httpClient.GetStringAsync(absoluteUrl, ct);
        return RadHtmlParser.ParseCatalogItems(html)
            .Select(x => x.LotUrl)
            .Distinct()
            .ToList();
    }
}
