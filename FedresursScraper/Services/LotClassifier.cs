using Azure.AI.OpenAI;
using Azure.AI.Inference;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ClientModel;
using Azure;

public class LotClassifier : ILotClassifier
{
    private readonly ILogger<LotClassifier> _logger;
    private readonly ChatClient _chatClient;
    private readonly string _modelName = "deepseek-chat";
    private readonly List<string> _categories;

    public LotClassifier(ILogger<LotClassifier> logger, string apiKey)
    {
        _logger = logger;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.deepseek.com/v1")
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(_modelName);

        _categories = new List<string> {
            "Квартира",
            "Жилой дом",
            "Прочие постройки",
            "Нежилое помещение",
            "Нежилое здание",
            "Имущественный комплекс",
            "Иные сооружения",
            "Земельный участок",
            "Объекты с/х недвижимости",
            "Легковой автомобиль",
            "Коммерческий транспорт и спецтехника",
            "Мототехника",
            "Водный транспорт",
            "Авиатранспорт",
            "С/х техника",
            "Иной транспорт и техника",
            "Промышленное оборудование",
            "Строительное оборудование",
            "Складское оборудование",
            "Торговое оборудование",
            "Металлообрабатывающее оборудование",
            "Пищевое оборудование",
            "Деревообрабатывающее оборудование",
            "Производственные линии",
            "Другое оборудование",
            "Товарно-материальные ценности",
            "Прочее",
            "Дебиторская задолженность",
            "Ценные бумаги"
        };
    }

    public async Task<string> ClassifyLotAsync(string lotTitle)
    {
        string categoryList = string.Join(", ", _categories);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — полезный ассистент, который помогает классифицировать лоты."),
            new UserChatMessage($"Классифицируй название лота в одну из следующих категорий: [{categoryList}]. Категория «Прочие постройки» лучше всего подходит для классификации таких вспомогательных строений, как бани, сараи, гаражи, хозяйственные блоки и беседки. Под категорию «Инвестиционный проект» подходит готовый бизнес, аренда, сервис, продажи - лот генерирует прибыль. Если в описании лота прямо указана какая-либо категория, то включай ее в ответ. Название лота: '{lotTitle}'. В ответе верни только название категории. Если лот подходит под несколько категорий, то верни их через запятую.")
        };

        var chatCompletionOptions = new ChatCompletionOptions()
        {
            //MaxTokens = 20,
            Temperature = 0.0f // Устанавливаем Temperature=0 для более детерминированного ответа
        };
        //chatCompletionOptions.SetMaxTokens(20);

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions);

            string content = response.Value.Content.First().Text;
            return content.Trim();
        }
        catch (ClientResultException ex)
        {
            _logger.LogCritical(ex, "Произошла ошибка при вызове API DeepSeek");
            return "Ошибка классификации";
        }
    }
}
