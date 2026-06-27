using System.ClientModel;
using FedresursScraper.Services;
using Lots.Application.Interfaces;
using Lots.Application.Services.DeepSeek;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Lots.Application.Services;

public class LotPropertyDescriptionSummarizer : ILotPropertyDescriptionSummarizer
{
    private readonly ILogger<LotPropertyDescriptionSummarizer> _logger;
    private readonly ChatClient _chatClient;
    private readonly IDeepSeekBudgetGuard _budgetGuard;
    private readonly int _inputMaxChars;
    private const string ModelName = "deepseek-chat";

    public LotPropertyDescriptionSummarizer(
        ILogger<LotPropertyDescriptionSummarizer> logger,
        IConfiguration configuration,
        IDeepSeekBudgetGuard budgetGuard)
    {
        _logger = logger;
        _budgetGuard = budgetGuard;
        _inputMaxChars = configuration.GetValue("LotDescription:SummarizeInputMaxChars", 14000);

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
        _chatClient = openAiClient.GetChatClient(ModelName);
    }

    public async Task<PropertyDescriptionSummaryResult> SummarizeAsync(
        string rawText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new PropertyDescriptionSummaryResult { Error = "Пустой текст документа." };
        }

        await _budgetGuard.AcquireRequestSlotAsync("description-summary", cancellationToken);

        var preparedText = LotPropertyDocumentHelper.PrepareTextForSummarization(rawText, _inputMaxChars);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "Ты помогаешь составлять описания имущества для каталога торгов по банкротству в России. " +
                "Отвечай только текстом описания на русском языке, без заголовков и markdown."),
            new UserChatMessage(
                "Ниже — текст из документа (опись, перечень имущества лота). " +
                "Составь краткое человекочитаемое описание для карточки лота (3–8 предложений, до 1500 символов).\n\n" +
                "Правила:\n" +
                "- укажи тип имущества и общий характер состава лота;\n" +
                "- если много однотипных позиций или номенклатур — укажи количество, категории и примеры, НЕ перечисляй всё подряд;\n" +
                "- сохрани важные адреса, площади, марки, модели, если они есть;\n" +
                "- не включай юридические шаблоны, реквизиты и порядок ознакомления.\n\n" +
                $"ТЕКСТ ДОКУМЕНТА:\n{preparedText}")
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(
                messages,
                new ChatCompletionOptions { Temperature = 0.2f },
                cancellationToken);

            if (response.Value.Usage?.TotalTokenCount is > 0)
            {
                await _budgetGuard.RecordTokenUsageAsync(
                    "description-summary",
                    response.Value.Usage.TotalTokenCount,
                    cancellationToken);
            }

            var content = response.Value.Content?.FirstOrDefault()?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return new PropertyDescriptionSummaryResult
                {
                    Error = "DeepSeek вернул пустой ответ.",
                };
            }

            return new PropertyDescriptionSummaryResult
            {
                Summary = content,
                IsSummarized = true,
            };
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "DeepSeek недоступен при обобщении описания.");
            return new PropertyDescriptionSummaryResult { Error = ex.Message };
        }
        catch (ClientResultException ex) when (ex.Status == 402)
        {
            await _budgetGuard.TripOnPaymentFailureAsync(cancellationToken);
            return new PropertyDescriptionSummaryResult { Error = "Баланс DeepSeek исчерпан." };
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            await _budgetGuard.TripOnRateLimitAsync(cancellationToken);
            return new PropertyDescriptionSummaryResult { Error = "Превышен лимит запросов DeepSeek." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обобщения описания имущества через DeepSeek.");
            return new PropertyDescriptionSummaryResult { Error = "Не удалось обобщить описание через ИИ." };
        }
    }
}
