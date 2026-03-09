using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using System.Text.RegularExpressions;

namespace FedresursScraper.Services;

public class RadParserService
{
    private readonly HttpClient _httpClient;
    private readonly IBrowsingContext _browsingContext;

    public RadParserService(HttpClient httpClient)
    {
        _httpClient = httpClient;

        // Настройка AngleSharp
        var config = Configuration.Default;
        _browsingContext = BrowsingContext.New(config);
    }

    /// <summary>
    /// Парсит список ссылок на лоты с текущей страницы каталога
    /// </summary>
    public async Task<List<string>> GetLotUrlsFromCatalogAsync(string catalogUrl)
    {
        var response = await _httpClient.GetStringAsync(catalogUrl);
        //Console.WriteLine("ОТВЕТ СЕРВЕРА: " + response.Substring(0, Math.Min(response.Length, 500)));
        
        var document = await _browsingContext.OpenAsync(req => req.Content(response));

        var lotUrls = document.QuerySelectorAll("a")
            .Select(a => a.GetAttribute("href"))
            .Where(href => !string.IsNullOrEmpty(href) &&
                           href.Contains("dispatch=products.view") &&
                           href.Contains("product_id="))
            .Select(href => href!.StartsWith("http") ? href : $"https://catalog.lot-online.ru{href}")
            .Distinct()
            .ToList();

        return lotUrls;
    }
}