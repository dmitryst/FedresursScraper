using System.ClientModel;
using System.Text.RegularExpressions;
using Lots.Application.Interfaces;
using Lots.Application.Services.DeepSeek;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Lots.Application.Services;

public class LotDescriptionSplitter : ILotDescriptionSplitter
{
    private readonly ILogger<LotDescriptionSplitter> _logger;
    private readonly ChatClient _chatClient;
    private readonly IDeepSeekBudgetGuard _budgetGuard;
    
    private static readonly Regex ViewingProcedureRegex = new Regex(
        @"(?i)(Ознакомление с (имуществом|предметом|лотом|документами).*?[:.]|С (имуществом|предметом торгов|лотом) можно ознакомиться|Порядок (и время )?ознакомления|Осмотр (имущества|предмета|лота).*?[:.]|Для ознакомления с.*?[:.])", 
        RegexOptions.Compiled);

    private static readonly Regex SuspiciousWordsRegex = new Regex(
        @"(?i)(ознакомл|осмотр|тел\.|телефон|e-mail|email|заявк)", 
        RegexOptions.Compiled);

    public LotDescriptionSplitter(
        ILogger<LotDescriptionSplitter> logger,
        IConfiguration configuration,
        IDeepSeekBudgetGuard budgetGuard)
    {
        _logger = logger;
        _budgetGuard = budgetGuard;

        var apiKey = configuration["DeepSeek:ApiKey"]
            ?? throw new InvalidOperationException("DeepSeek:ApiKey not found");
        var apiUrl = configuration["DeepSeek:ApiUrl"]
            ?? throw new InvalidOperationException("DeepSeek:ApiUrl not found");

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(apiUrl),
            NetworkTimeout = TimeSpan.FromMinutes(2),
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient("deepseek-chat");
    }

    public async Task<LotDescriptionSplitResult> SplitAsync(string rawText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new LotDescriptionSplitResult { Description = string.Empty, ViewingProcedure = string.Empty };
        }

        rawText = rawText.Trim();

        // 1. Попытка быстрого разделения с помощью Regex
        var match = ViewingProcedureRegex.Match(rawText);
        if (match.Success)
        {
            return new LotDescriptionSplitResult
            {
                Description = rawText[..match.Index].Trim(),
                ViewingProcedure = rawText[match.Index..].TrimStart(':', '.', ',', ' ').Trim(),
                UsedLlm = false
            };
        }

        // 2. Проверка на наличие "подозрительных" слов
        if (!SuspiciousWordsRegex.IsMatch(rawText))
        {
            // Если нет маркеров и подозрительных слов, возвращаем как есть
            return new LotDescriptionSplitResult
            {
                Description = rawText,
                ViewingProcedure = string.Empty,
                UsedLlm = false
            };
        }

        // 3. Если есть подозрительные слова, но не сработали регулярки, используем LLM
        return await SplitWithLlmAsync(rawText, cancellationToken);
    }

    private async Task<LotDescriptionSplitResult> SplitWithLlmAsync(string rawText, CancellationToken cancellationToken)
    {
        await _budgetGuard.AcquireRequestSlotAsync("description-split", cancellationToken);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "Ты помогаешь парсить описания лотов с торгов по банкротству. " +
                "Твоя задача: разделить текст на две части: 'description' (само имущество, характеристики) и 'viewing_procedure' (порядок осмотра, контакты, сроки, реквизиты). " +
                "Верни ответ строго в формате JSON, без лишнего текста, с ключами 'description' и 'viewing_procedure'."),
            new UserChatMessage($"Раздели текст:\n\n{rawText}")
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(
                messages,
                new ChatCompletionOptions 
                { 
                    Temperature = 0.1f,
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
                },
                cancellationToken);

            if (response.Value.Usage?.TotalTokenCount is > 0)
            {
                await _budgetGuard.RecordTokenUsageAsync(
                    "description-split",
                    response.Value.Usage.TotalTokenCount,
                    cancellationToken);
            }

            var content = response.Value.Content?.FirstOrDefault()?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Простейший парсинг JSON, так как мы требуем строгий формат
                var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;
                
                string description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";
                string viewingProcedure = root.TryGetProperty("viewing_procedure", out var viewProp) ? viewProp.GetString() ?? "" : "";

                return new LotDescriptionSplitResult
                {
                    Description = description.Trim(),
                    ViewingProcedure = viewingProcedure.Trim(),
                    UsedLlm = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при разделении текста через LLM");
        }

        // В случае ошибки LLM возвращаем оригинальный текст
        return new LotDescriptionSplitResult
        {
            Description = rawText,
            ViewingProcedure = string.Empty,
            UsedLlm = true // Помечаем, что пытались использовать LLM
        };
    }
}