using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using FedresursScraper.Services.Models;

namespace FedresursScraper.Services;

public interface ILotEvaluationService
{
    Task<LotEvaluationResult?> EvaluateLotAsync(Guid lotId);
}

public class LotEvaluationService : ILotEvaluationService
{
    private readonly ILogger<LotEvaluationService> _logger;
    private readonly ChatClient _chatClient;
    private readonly LotsDbContext _dbContext;
    private readonly string _modelName = "deepseek-reasoner"; // Используем R1 (reasoning)

    public LotEvaluationService(
        ILogger<LotEvaluationService> logger,
        IConfiguration configuration,
        LotsDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;

        string apiKey = configuration["DeepSeek:ApiKey"] ?? throw new InvalidOperationException("API Key not found");
        string apiUrl = configuration["DeepSeek:ApiUrl"] ?? throw new InvalidOperationException("API URL not found");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(apiUrl),
            NetworkTimeout = TimeSpan.FromMinutes(5), // R1 может думать долго
        };

        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(_modelName);
    }

    public async Task<LotEvaluationResult?> EvaluateLotAsync(Guid lotId)
    {
        var lot = await _dbContext.Lots
            .Include(l => l.Bidding)
            .Include(l => l.Categories)
            .Include(l => l.CadastralInfos)
            .FirstOrDefaultAsync(l => l.Id == lotId);

        if (lot == null)
        {
            _logger.LogWarning("Лот {LotId} не найден", lotId);
            return null;
        }

        var description = lot.Description;
        var title = lot.Title;
        var startPrice = lot.StartPrice;
        var region = lot.PropertyRegionName;

        // Формируем блок с данными из Росреестра
        var cadastralSb = new System.Text.StringBuilder();
        if (lot.CadastralInfos != null && lot.CadastralInfos.Any())
        {
            cadastralSb.AppendLine("Данные из Росреестра (кадастровая информация):");
            foreach (var info in lot.CadastralInfos)
            {
                cadastralSb.Append($"- Кадастровый номер: {info.CadastralNumber}");
                if (info.Area.HasValue) cadastralSb.Append($", Площадь: {info.Area.Value}");
                if (info.CadastralCost.HasValue) cadastralSb.Append($", Кадастровая стоимость: {info.CadastralCost.Value}");
                if (!string.IsNullOrWhiteSpace(info.Category)) cadastralSb.Append($", Категория: {info.Category}");
                if (!string.IsNullOrWhiteSpace(info.PermittedUse)) cadastralSb.Append($", ВРИ: {info.PermittedUse}");
                if (!string.IsNullOrWhiteSpace(info.Address)) cadastralSb.Append($", Адрес: {info.Address}");
                if (!string.IsNullOrWhiteSpace(info.Status)) cadastralSb.Append($", Статус: {info.Status}");
                cadastralSb.AppendLine();
            }
        }
        else
        {
            cadastralSb.AppendLine("Данные из Росреестра отсутствуют.");
        }

        var prompt = $@"
Проанализируй данный лот с торгов по банкротству.
Название: {title}
Описание: {description}
Начальная цена торгов: {startPrice} руб.
Регион: {region}
Категории: {string.Join(", ", lot.Categories.Select(c => c.Name))}

{cadastralSb.ToString()}

ИНСТРУКЦИЯ ПО ОЦЕНКЕ СТОИМОСТИ:
1. 'Начальная цена торгов' — твой главный ориентир для оценки рыночной стоимости.
2. 'Кадастровая стоимость' (если есть) дана справочно. Если она расходится с начальной ценой в несколько раз (что часто бывает в РФ), приоритет отдавай начальной цене как более близкой к рыночной реальности.
3. Если есть данные о площади (из описания или Росреестра) и локации, рассчитай примерную стоимость за квадратный метр (или сотку для земли) и умножь на площадь. 
4. Сделай поправки на состояние объекта, риски торгов, обременения.

Твоя задача:
1. Провести детальный пошаговый анализ (reasoning). Опиши логику расчета стоимости по шагам: оценка локации, вычисление стоимости за единицу площади (если применимо), сравнение начальной и кадастровой стоимости.
2. Оценить реальную рыночную стоимость (estimatedPrice) одним числом в рублях.
3. Оценить ликвидность по шкале от 1 до 10 (где 10 - уйдет моментально, 1 - невозможно продать).
4. Написать краткое инвестиционное резюме (Investment Summary).

ФОРМАТ ОТВЕТА:
Сначала выведи текст с пошаговым рассуждением (step-by-step reasoning).
В конце ответа ОБЯЗАТЕЛЬНО выведи JSON блок следующего формата:

```json
{{
    ""estimatedPrice"": 1234567,
    ""liquidityScore"": 7,
    ""investmentSummary"": ""Текст резюме...""
}}
```
";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — профессиональный инвестиционный аналитик по торгам по банкротству."),
            new UserChatMessage(prompt)
        };

        try
        {
            // DeepSeek R1 не поддерживает response_format json_object вместе с thinking в текущей версии API (обычно),
            // поэтому просим текстом и парсим.
            var completion = await _chatClient.CompleteChatAsync(messages);

            if (completion.Value.Content == null || completion.Value.Content.Count == 0)
            {
                throw new Exception("Пустой ответ от DeepSeek");
            }

            var fullText = completion.Value.Content[0].Text;
            var usage = completion.Value.Usage;

            // Парсим ответ
            var (reasoning, jsonPart) = ParseResponse(fullText);

            if (string.IsNullOrEmpty(jsonPart))
            {
                _logger.LogError("Не удалось найти JSON в ответе модели: {Response}", fullText);
                throw new Exception("JSON не найден в ответе");
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var resultDto = JsonSerializer.Deserialize<EvaluationJsonDto>(jsonPart, options);

            if (resultDto == null) throw new Exception("Ошибка десериализации JSON");

            var result = new LotEvaluationResult
            {
                EstimatedPrice = resultDto.EstimatedPrice,
                LiquidityScore = resultDto.LiquidityScore,
                InvestmentSummary = resultDto.InvestmentSummary,
                ReasoningText = reasoning.Trim(),
                PromptTokens = usage.InputTokenCount,
                CompletionTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                // Пытаемся достать детализацию токенов (требуется свежая версия пакета OpenAI)
                ReasoningTokens = usage.OutputTokenDetails?.ReasoningTokenCount ?? 0
            };

            // Сохраняем в БД
            var entity = new LotEvaluation
            {
                LotId = lot.Id,
                EstimatedPrice = result.EstimatedPrice,
                LiquidityScore = result.LiquidityScore,
                InvestmentSummary = result.InvestmentSummary,
                ReasoningText = result.ReasoningText,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                ReasoningTokens = result.ReasoningTokens,
                TotalTokens = result.TotalTokens,
                ModelName = _modelName
            };

            _dbContext.LotEvaluations.Add(entity);
            await _dbContext.SaveChangesAsync();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при оценке лота {LotId}", lotId);
            throw;
        }
    }

    private (string reasoning, string json) ParseResponse(string text)
    {
        // Ищем блок ```json ... ```
        var jsonStart = text.IndexOf("```json");
        var jsonEnd = text.LastIndexOf("```");

        if (jsonStart != -1 && jsonEnd > jsonStart)
        {
            var reasoning = text.Substring(0, jsonStart);
            var json = text.Substring(jsonStart + 7, jsonEnd - (jsonStart + 7));
            return (reasoning, json);
        }

        // Попытка найти просто { } в конце
        var lastBrace = text.LastIndexOf('}');
        var firstBrace = text.LastIndexOf('{', lastBrace); // Ищем открывающую скобку JSON-объекта

        if (lastBrace != -1 && firstBrace != -1)
        {
            var json = text.Substring(firstBrace, lastBrace - firstBrace + 1);
            var reasoning = text.Substring(0, firstBrace);
            return (reasoning, json);
        }

        return (text, "");
    }

    private class EvaluationJsonDto
    {
        public decimal EstimatedPrice { get; set; }
        public int LiquidityScore { get; set; }
        public string InvestmentSummary { get; set; } = string.Empty;
    }
}
