using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FedresursScraper.Integrations.Fedresurs.Models;

namespace FedresursScraper.Integrations.Fedresurs.Clients;

public class FedresursApiClient : IFedresursApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FedresursApiClient> _logger;
    private readonly FedresursApiOptions _options;

    // Кэширование токена
    private string? _cachedJwt;
    private DateTime _tokenExpiration = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public FedresursApiClient(
        HttpClient httpClient,
        ILogger<FedresursApiClient> logger,
        IOptions<FedresursApiOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<MessagesResponse?> GetMessagesAsync(
        DateTime dateBegin,
        DateTime dateEnd,
        string[] types,
        int offset = 0,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        // Безопасно кодируем параметры для URL
        var typesQuery = Uri.EscapeDataString(string.Join(",", types));
        var beginStr = Uri.EscapeDataString($"gte:{dateBegin:yyyy-MM-ddTHH:mm:ss}");
        var endStr = Uri.EscapeDataString($"lte:{dateEnd:yyyy-MM-ddTHH:mm:ss}");

        var url = $"v1/messages?type={typesQuery}&datePublishBegin={beginStr}&datePublishEnd={endStr}&includeContent=true&limit={limit}&offset={offset}";

        _logger.LogInformation("Запрос к ЕФРСБ: {Url}", url);

        var response = await _httpClient.GetAsync(url, cancellationToken);

        // Если статус не 2xx, читаем тело ошибки от Федресурса
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Ошибка Федресурса HTTP {StatusCode}. Подробности: {ErrorContent}", (int)response.StatusCode, errorContent);

            // После логирования кидаем исключение, чтобы воркер ушел на паузу
            response.EnsureSuccessStatusCode();
        }

        return await response.Content.ReadFromJsonAsync<MessagesResponse>(cancellationToken: cancellationToken);
    }

    public async Task<List<LinkedMessageDto>?> GetLinkedMessagesAsync(Guid messageGuid, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync(cancellationToken);

        var url = $"v1/messages/{messageGuid}/linked";

        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<LinkedMessageDto>(); // Если связанных нет, API может вернуть 404
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LinkedMessageDto>>(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Проверяет токен и обновляет его, если он истек или его нет.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        // Если токен еще жив (с запасом в 5 минут), ничего не делаем
        if (!string.IsNullOrEmpty(_cachedJwt) && DateTime.UtcNow.AddMinutes(5) < _tokenExpiration)
        {
            return;
        }

        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Повторная проверка внутри lock (Double-check locking)
            if (!string.IsNullOrEmpty(_cachedJwt) && DateTime.UtcNow.AddMinutes(5) < _tokenExpiration)
            {
                return;
            }

            _logger.LogInformation("Получение нового авторизационного токена Федресурса...");

            var requestBody = new { login = _options.Login, password = _options.Password };
            var response = await _httpClient.PostAsJsonAsync("v1/auth", requestBody, cancellationToken);

            response.EnsureSuccessStatusCode();

            var authResult = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: cancellationToken);

            if (authResult?.Jwt == null)
            {
                throw new InvalidOperationException("Не удалось получить JWT токен из ответа.");
            }

            _cachedJwt = authResult.Jwt;
            // Токен живет 8 часов по доке. Ставим 7.5 часов для перестраховки
            _tokenExpiration = DateTime.UtcNow.AddHours(7.5);

            // Устанавливаем токен для всех последующих запросов этого HttpClient
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedJwt);

            _logger.LogInformation("Токен успешно получен и закеширован до {Expiration:O}", _tokenExpiration);
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }
}