using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace FedresursScraper.Services;

public class VehicleAttributesExtractor : IVehicleAttributesExtractor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VehicleAttributesExtractor> _logger;
    private readonly ChatClient _chatClient;
    private readonly string _modelName = "deepseek-chat";
    
    // Минимальный интервал между запросами
    private readonly TimeSpan _minRequestInterval;
    private static DateTime _nextAllowedRequestTime = DateTime.MinValue;
    private static readonly object _lockObj = new object();

    public VehicleAttributesExtractor(
        IServiceScopeFactory scopeFactory,
        ILogger<VehicleAttributesExtractor> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        string apiKey = configuration["DeepSeek:ApiKey"] ?? throw new InvalidOperationException("API Key not found");
        string apiUrl = configuration["DeepSeek:ApiUrl"] ?? throw new InvalidOperationException("API URL not found");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(apiUrl),
            NetworkTimeout = TimeSpan.FromMinutes(2),
        };

        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(_modelName);

        double seconds = configuration.GetValue<double>("DeepSeek:RequestIntervalSeconds", 3.0);
        _minRequestInterval = TimeSpan.FromSeconds(seconds);
    }

    public async Task ExtractAttributesForActiveVehiclesAsync(CancellationToken token)
    {
        _logger.LogInformation("Запуск фоновой задачи по извлечению атрибутов для транспортных средств.");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var vehicleCategories = new[]
        {
            "Легковой автомобиль"
        };

        // Ищем активные лоты из категории транспорта, у которых еще нет заполненных атрибутов (например, brand)
        var lotsToProcess = await dbContext.Lots
            .Include(l => l.Categories)
            .Where(Lot.IsActiveExpression)
            .Where(l => l.Categories.Any(c => vehicleCategories.Contains(c.Name)))
            // Для JSONB в EF Core используем EF.Functions.JsonContains или проверяем на null
            // Так как EF Core 9 может не транслировать ContainsKey для Dictionary, 
            // мы используем проверку, что атрибуты вообще не заполнены, 
            // либо загружаем все и фильтруем в памяти (так как активных лотов не очень много)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(token);

        // Фильтруем в памяти те, у которых нет ключа "_attributes_parsed"
        lotsToProcess = lotsToProcess
            .Where(l => l.Attributes == null || !l.Attributes.ContainsKey("_attributes_parsed"))
            .ToList();

        _logger.LogInformation("Найдено {Count} лотов для извлечения атрибутов транспорта.", lotsToProcess.Count);

        if (lotsToProcess.Count == 0)
        {
            return;
        }

        // Если лотов меньше 30, мы просто ждем, пока накопится больше, чтобы экономить токены.
        int minBatchSize = 30;
        if (lotsToProcess.Count < minBatchSize)
        {
            _logger.LogInformation("Найдено всего {Count} лотов. Ждем накопления до {MinBatchSize}.", lotsToProcess.Count, minBatchSize);
            return;
        }

        int batchSize = 30;
        for (int i = 0; i < lotsToProcess.Count; i += batchSize)
        {
            var batch = lotsToProcess.Skip(i).Take(batchSize).ToList();
            _logger.LogInformation("Обработка батча {BatchNumber} из {TotalBatches} (размер: {BatchSize})", 
                (i / batchSize) + 1, 
                (int)Math.Ceiling(lotsToProcess.Count / (double)batchSize), 
                batch.Count);

            await ProcessBatchAsync(batch, dbContext, token);
        }

        _logger.LogInformation("Фоновая задача по извлечению атрибутов завершена.");
    }

    private async Task ProcessBatchAsync(List<Lot> batch, LotsDbContext dbContext, CancellationToken token)
    {
        TimeSpan delay;
        lock (_lockObj)
        {
            var now = DateTime.UtcNow;
            if (_nextAllowedRequestTime < now)
            {
                _nextAllowedRequestTime = now;
            }
            delay = _nextAllowedRequestTime - now;
            _nextAllowedRequestTime = _nextAllowedRequestTime.Add(_minRequestInterval);
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, token);
        }

        var lotsListBuilder = new System.Text.StringBuilder();
        foreach (var lot in batch)
        {
            lotsListBuilder.AppendLine($"ЛОТ ID: {lot.Id}");
            lotsListBuilder.AppendLine(lot.Title ?? lot.Description);
            lotsListBuilder.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — эксперт по анализу имущества на торгах по банкротству. Твоя задача - извлекать характеристики транспортных средств из текста."),
            new UserChatMessage(
                $"Проанализируй описания {batch.Count} лотов и извлеки для каждого марку, модель, год выпуска и пробег.\n\n" +
                $"ОПИСАНИЯ ЛОТОВ:\n{lotsListBuilder}\n" +
                "ИНСТРУКЦИИ:\n" +
                "1. Для каждого лота извлеки 'brand' (Марка), 'model' (Модель), 'year' (Год выпуска, только цифры), 'mileage' (Пробег в км, только цифры).\n" +
                "2. Если какой-то параметр не указан в тексте, верни пустую строку \"\".\n" +
                "3. Год выпуска и пробег должны содержать только цифры. Удали слова 'год', 'г.в.', 'км' и пробелы между тысячами.\n" +
                "4. ВАЖНОЕ ПРАВИЛО ДЛЯ НАЗВАНИЙ: Для зарубежных марок и моделей (например, Honda, Toyota, BMW, Ford, Hyundai, Kia, Volkswagen и т.д.) используй ТОЛЬКО оригинальные англоязычные названия (латиницей), даже если в тексте они написаны по-русски (например, 'Хонда Цивик' -> brand: 'Honda', model: 'Civic'). Для отечественных марок (например, ВАЗ, ГАЗ, УАЗ, КАМАЗ, Москвич, Lada) используй ТОЛЬКО русскоязычные названия (кириллицей) или их общепринятые российские аббревиатуры (например, 'Лада Веста' -> brand: 'LADA', model: 'Vesta', 'ВАЗ 2107' -> brand: 'ВАЗ', model: '2107').\n\n" +
                "ФОРМАТ ОТВЕТА (JSON объект, где ключи - это ID лотов в формате строки):\n" +
                "{\n" +
                string.Join(",\n", batch.Select(l => $"  \"{l.Id}\": {{ \"brand\": \"\", \"model\": \"\", \"year\": \"\", \"mileage\": \"\" }}")) +
                "\n}\n\n" +
                $"ВАЖНО: Верни JSON объект с {batch.Count} элементами, где каждый ключ - это ID лота.")
        };

        var chatCompletionOptions = new ChatCompletionOptions()
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions);

            if (response.Value.Content == null || response.Value.Content.Count == 0)
            {
                _logger.LogWarning("DeepSeek вернул пустой ответ (без контента) для батча атрибутов.");
                return;
            }

            string content = response.Value.Content.First().Text;
            content = content.Trim().Replace("```json", "").Trim().Replace("```", "").Trim();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var batchResult = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(content, options);

            if (batchResult == null)
            {
                _logger.LogWarning("Не удалось десериализовать батч результатов атрибутов.");
                return;
            }

            foreach (var lot in batch)
            {
                lot.Attributes ??= new Dictionary<string, string>();
                
                // Ставим системный флаг, что лот был обработан ИИ, 
                // чтобы не обрабатывать его повторно, даже если ИИ ничего не нашел
                lot.Attributes["_attributes_parsed"] = "true";

                if (batchResult.TryGetValue(lot.Id.ToString(), out var extractedAttributes))
                {
                    foreach (var kvp in extractedAttributes)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            lot.Attributes[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }

            await dbContext.SaveChangesAsync(token);
            _logger.LogInformation("Успешно сохранены атрибуты для батча из {Count} лотов.", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении атрибутов для батча.");
        }
    }
}
