using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FedresursScraper.Services.DebtScoring;

public interface IOcrServiceClient
{
    Task<OcrResult> RecognizeAsync(byte[] fileContent, string fileName, CancellationToken cancellationToken = default);
}

public sealed class OcrResult
{
    public bool Success { get; init; }

    public string? Text { get; init; }

    public double? Confidence { get; init; }

    public string? Error { get; init; }
}

public class OcrServiceClient : IOcrServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OcrServiceClient> _logger;

    public OcrServiceClient(HttpClient httpClient, ILogger<OcrServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<OcrResult> RecognizeAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(fileContent), "file", fileName);

            using var response = await _httpClient.PostAsync("/ocr", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return new OcrResult
                {
                    Success = false,
                    Error = $"OCR HTTP {(int)response.StatusCode}: {body}",
                };
            }

            var payload = await response.Content.ReadFromJsonAsync<OcrResponseDto>(cancellationToken);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Text))
            {
                return new OcrResult { Success = false, Error = "OCR вернул пустой ответ" };
            }

            return new OcrResult
            {
                Success = true,
                Text = payload.Text,
                Confidence = payload.Confidence,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCR-сервис недоступен для файла {FileName}", fileName);
            return new OcrResult { Success = false, Error = ex.Message };
        }
    }

    private sealed class OcrResponseDto
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }
    }
}
