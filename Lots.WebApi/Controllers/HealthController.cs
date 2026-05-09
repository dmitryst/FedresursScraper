// Файл: /Controllers/HealthController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;

namespace FedresursScraper.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public HealthController(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Возвращает текущую версию бэкенд-приложения и скрапера.
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public async Task<IActionResult> GetVersion()
    {
        var webApiVersion = _configuration["AppInfo:Version"] ?? "unknown";
        var scraperVersion = "unknown";

        try
        {
            var scraperUrl = _configuration["ParserServiceUrl"] ?? "http://localhost:5001";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = System.TimeSpan.FromSeconds(2);
            var response = await client.GetAsync($"{scraperUrl.TrimEnd('/')}/api/health/version");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("version", out var versionElement) || doc.RootElement.TryGetProperty("Version", out versionElement))
                {
                    scraperVersion = versionElement.GetString() ?? "unknown";
                }
            }
        }
        catch
        {
            scraperVersion = "unavailable";
        }

        return Ok(new { WebApiVersion = webApiVersion, ScraperVersion = scraperVersion });
    }
}
