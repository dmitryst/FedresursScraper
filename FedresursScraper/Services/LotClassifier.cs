using Azure.AI.OpenAI;
using Azure.AI.Inference;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ClientModel;
using Azure;
using System.Text.Json;

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
            "Медицинское оборудование",
            "Пищевое оборудование",
            "Деревообрабатывающее оборудование",
            "Производственные линии",
            "Другое оборудование",
            "Другое оборудование",
            "Компьютеры и комплектующие",
            "Оргтехника",
            "Сетевое оборудование",
            "Дебиторская задолженность",
            "Ценные бумаги",
            "Доли в уставном капитале",
            "Товарно-материальные ценности",
            "Оружие",
            "Предметы искусства",
            "Драгоценности",
            "Прочее",
        };
    }

    public async Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription)
    {
        string categoryList = string.Join(", ", _categories);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — полезный ассистент, который анализирует описания лотов с торгов по банкротству."),
            new UserChatMessage(
                $"Описание лота:\n" +
                $"{lotDescription}\n\n" +
                "У тебя 3 задачи:\n" +
                $"1. Классифицируй описание лота в одну из следующих категорий: {categoryList}.\n" +
                "Категория «Прочие постройки» лучше всего подходит для классификации таких вспомогательных строений \n" +
                "как бани, сараи, гаражи, хозяйственные блоки и беседки. Под категорию «Инвестиционный проект» подходит \n" +
                "готовый бизнес, аренда, сервис, продажи - лот генерирует прибыль. В категорию «Коммерческий транспорт \n" +
                "и спецтехника» входят грузовики, прицепы, ГАЗели, бетономешалки, автобусы, экскаваторы-погрузчики, \n" +
                "бульдозеры, краны, погрузчики, грейдеры и т.п. В категорию «Нежилые помещения» относятся в том числе \n" +
                "склады, зерносклады. Если в описании лота прямо указана какая-либо категория, то включай ее в ответ. \n" +
                "Если лот подходит под несколько категорий, то верни их через запятую.\n" +
                "2. Из описания лота необходимо сформировать его название. Название должно содержать только самые \n" +
                "важные характеристики лота, подходящие под его категорию.\n" +
                "3. Определить является ли лот долей. Установи `true`, если в описании упоминается долевая собственность \n" +
                "(например, 'доля 1/2', '50/100 доли'), иначе `false`. Совместная нажитая собственность не является долей.\n\n" +
                "Ответ должен быть в формате json. Пример ответа:\n" +
                "{\n" +
                "   \"categories\": [\"Земельный участок\", \"Жилой дом\"],\n" +
                "   \"title\": \"Жилой дом 420 кв.м. в Московской области, д. Жостово и земельный участок 1500 кв.м. КН 19:06:081003:78\",\n" +
                "   \"isSharedOwnership\": false\n" +
                "}"
            )
        };

        var chatCompletionOptions = new ChatCompletionOptions()
        {
            Temperature = 0.0f, // Устанавливаем Temperature=0 для более детерминированного ответа
        };

        string content = string.Empty;
        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions);

            content = response.Value.Content.First().Text;
            //_logger.LogInformation("Получен сырой ответ от DeepSeek: {Content}", content);

            content = content.Trim().Replace("```json", "").Trim().Replace("```", "").Trim();
            _logger.LogInformation("Очищенный JSON для десериализации: {Content}", content);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<LotClassificationResult>(content, options);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogCritical(jsonEx, "Ошибка десериализации JSON. Контент после очистки: {Content}", content);
            return null;
        }
        catch (ClientResultException ex)
        {
            _logger.LogCritical(ex, "Произошла ошибка при вызове API DeepSeek");
            return null;
        }
    }
}
