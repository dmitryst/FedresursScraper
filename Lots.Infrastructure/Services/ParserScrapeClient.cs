using System.Net.Http.Json;
using FedresursScraper.Services.Models;
using Lots.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FedresursScraper.Services;

public class ParserScrapeClient : IParserScrapeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParserScrapeClient> _logger;

    public ParserScrapeClient(HttpClient httpClient, IConfiguration configuration, ILogger<ParserScrapeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var baseUrl = configuration["ParserServiceUrl"] ?? "http://localhost:5001";
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<BankruptMessageScrapeResult> GetBankruptMessageDataAsync(
        Guid bankruptMessageId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/scrape/bankruptmessages/{bankruptMessageId}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Parser scrape failed for {MessageId}: {Status} {Body}",
                bankruptMessageId,
                response.StatusCode,
                body);
            throw new InvalidOperationException(
                $"Не удалось загрузить лоты с Федресурса (HTTP {(int)response.StatusCode}).");
        }

        var result = await response.Content.ReadFromJsonAsync<BankruptMessageScrapeResult>(cancellationToken: cancellationToken);
        return result ?? new BankruptMessageScrapeResult();
    }
}
