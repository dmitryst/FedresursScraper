using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services;

public interface IIndexNowService
{
    Task SubmitUrlAsync(string url);
    Task SubmitUrlsAsync(IEnumerable<string> urls);
}

public class IndexNowService : IIndexNowService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IndexNowService> _logger;
    private readonly IConfiguration _configuration;
    
    private string Host => _configuration["IndexNow:Host"] ?? "s-lot.ru";
    private string Key => _configuration["IndexNow:Key"] ?? throw new InvalidOperationException("IndexNow:Key не найден в конфигурации");
    private string KeyLocation => $"https://{Host}/{Key}.txt";
    private string ApiUrl => _configuration["IndexNow:ApiUrl"] ?? "https://api.indexnow.org/indexnow";

    public IndexNowService(
        HttpClient httpClient, 
        ILogger<IndexNowService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SubmitUrlAsync(string url)
    {
        await SubmitUrlsAsync(new[] { url });
    }

    public async Task SubmitUrlsAsync(IEnumerable<string> urls)
    {
        var urlList = urls.ToList();
        
        if (!urlList.Any())
        {
            _logger.LogWarning("IndexNow: Список URL пуст, отправка пропущена.");
            return;
        }

        var requestData = new
        {
            host = Host,
            key = Key,
            keyLocation = KeyLocation,
            urlList = urlList
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestData), 
            Encoding.UTF8, 
            "application/json");

        try 
        {
            _logger.LogInformation("IndexNow: Отправка {Count} URL в {ApiUrl}", urlList.Count, ApiUrl);
            
            var response = await _httpClient.PostAsync(ApiUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("IndexNow: Успешно отправлено {Count} URL. Статус: {StatusCode}", 
                    urlList.Count, response.StatusCode);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("IndexNow: Ошибка отправки. Статус: {StatusCode}, Тело: {ErrorBody}", 
                    response.StatusCode, errorBody);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "IndexNow: Ошибка HTTP-запроса при отправке {Count} URL", urlList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IndexNow: Неожиданная ошибка при отправке URL");
        }
    }
}
