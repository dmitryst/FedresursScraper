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
    private readonly Dictionary<string, List<string>> _categoryTree;

    // Уточнения для конкретных категорий (Business Rules)
    private readonly Dictionary<string, string> _categoryHints;

    // Белый список чистых категорий для быстрой проверки
    private readonly HashSet<string> _validCategories;

    public LotClassifier(ILogger<LotClassifier> logger, string apiKey)
    {
        _logger = logger;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.deepseek.com/v1")
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(_modelName);

        // должно быть всегда в соответствии с constants.ts проекта app-lot
        _categoryTree = new Dictionary<string, List<string>>
        {
            { "Недвижимость", new List<string> {
                "Квартира", "Жилой дом", "Прочие постройки", "Нежилое помещение", "Нежилое здание",
                "Имущественный комплекс", "Иные сооружения", "Земельный участок", "Объекты с/х недвижимости"
            }},
            { "Готовый бизнес", new List<string>{
                "Готовый бизнес"
            } },
            { "Транспортные средства", new List<string> {
                "Легковой автомобиль", "Коммерческий транспорт и спецтехника", "Мототехника",
                "Водный транспорт", "Авиатранспорт", "С/х техника", "Иной транспорт и техника"
            }},
            { "Оборудование", new List<string> {
                "Промышленное оборудование", "Строительное оборудование", "Складское оборудование",
                "Торговое оборудование", "Металлообрабатывающее оборудование", "Медицинское оборудование",
                "Пищевое оборудование", "Деревообрабатывающее оборудование", "Производственные линии",
                "Сварочное оборудование", "Другое оборудование"
            }},
            { "Компьютерное оборудование", new List<string> {
                "Компьютеры и комплектующие", "Оргтехника", "Сетевое оборудование"
            }},
            { "Финансовые активы", new List<string> {
                "Дебиторская задолженность", "Ценные бумаги", "Доли в уставном капитале"
            }},
            { "Товарно-материальные ценности", new List<string> {
                "Одежда", "Мебель", "Строительные материалы", "Оружие",
                "Предметы искусства", "Драгоценности", "Другие ТМЦ"
            }},
            { "Нематериальные активы", new List<string> {
                "Программное обеспечение", "Торговые знаки", "Авторские права", "Патенты на изобретение",
                "Другие нематериальные активы"
            }},
            { "Прочее", new List<string> {
                "Прочее"
            }}
        };

        _categoryHints = new Dictionary<string, string>
        {
            { "Прочие постройки", "бани, сараи, гаражи, хозяйственные блоки, беседки" },
            { "Коммерческий транспорт и спецтехника", "грузовики, прицепы, автобусы, экскаваторы, бульдозеры, краны, погрузчики" },
            { "Нежилое помещение", "склады, зерносклады, офисы, магазины" },
            { "Имущественный комплекс", "готовый бизнес, базы отдыха, заводы целиком" },
            { "С/х техника", "тракторы, комбайны, сеялки (если это самоходная техника, а не оборудование)" },
            { "Готовый бизнес", "бизнес под ключ, арендный бизнес, сервис, продажи (торговля), лот приносит прибыль (действующее предприятие)" },
            { "Прочее", "присваивается, когда ни одна из вышеперечисленных категорий не подходит" }
        };

        // плоский список всех валидных имен категорий
        _validCategories = _categoryTree.Values
            .SelectMany(c => c)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription)
    {
        // Формируем "умный" список категорий с подсказками
        var categoriesPromptBuilder = new System.Text.StringBuilder();

        foreach (var group in _categoryTree)
        {
            categoriesPromptBuilder.AppendLine($"- Группа '{group.Key}':");
            foreach (var category in group.Value)
            {
                // Если для категории есть подсказка, добавляем её в скобках
                string line = _categoryHints.TryGetValue(category, out var hint)
                    ? $"  * {category} (включает: {hint})"
                    : $"  * {category}";

                categoriesPromptBuilder.AppendLine(line);
            }
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — эксперт по анализу имущества на торгах по банкротству."),

            // --- FEW-SHOT EXAMPLES (ОБУЧЕНИЕ НА ПРИМЕРАХ) ---
            // Пример 1: Недвижимость
            new UserChatMessage(
                "Описание: Лот №1. Земельный участок для ИЖС, площадь 1500 кв.м, кадастровый номер 50:00:000000:123, расположен по адресу: Московская обл, г. Химки. На участке расположен недостроенный дом."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Земельный участок\", \"Прочие постройки\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Земельный участок 15 сот. (ИЖС) с недостроем, Московская обл., г. Химки, КН 50:00:000000:123\",\n" +
                "  \"isSharedOwnership\": false\n" +
                "}"),

            // Пример 2: Транспорт
            new UserChatMessage(
                "Описание: Автомобиль легковой Toyota Camry, 2018 г.в., VIN X123456789, цвет черный, не на ходу, требуется ремонт двигателя."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Легковой автомобиль\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Toyota Camry, 2018 г.в. (требует ремонта)\",\n" +
                "  \"isSharedOwnership\": false\n" +
                "}"),
                
            // --- РЕАЛЬНЫЙ ЗАПРОС ---
            new UserChatMessage(
                $"Проанализируй описание лота и заполни JSON.\n\n" +
                $"ОПИСАНИЕ ЛОТА:\n{lotDescription}\n\n" +

                $"СПИСОК ДОПУСТИМЫХ КАТЕГОРИЙ:\n{categoriesPromptBuilder}\n\n" +

                "ИНСТРУКЦИИ:\n" +
                "1. Выбери категории СТРОГО из списка выше, учитывая пояснения в скобках.\n" +
                "2. Если лот подходит под несколько категорий, верни их списком.\n" +
                "3. Если ни одна категория не подходит, выбери 'Прочее' и заполни поле 'suggestedCategory' своим вариантом.\n" +
                "4. Сформируй название (title). Если это доля, укажи это в названии.\n" +
                "5. isSharedOwnership = true только для долевой собственности (1/2 и т.д.). Для совместно нажитой собственности isSharedOwnership = false\n\n" +

                "ФОРМАТ ОТВЕТА (JSON):\n" +
                "{ \"categories\": [], \"suggestedCategory\": null, \"title\": \"...\", \"isSharedOwnership\": false }")
        };

        var chatCompletionOptions = new ChatCompletionOptions()
        {
            Temperature = 0.1f, // Чуть выше 0, чтобы дать гибкость для suggestedCategory, но сохранить точность
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() // Гарантирует валидный JSON
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
            var result = JsonSerializer.Deserialize<LotClassificationResult>(content, options);

            if (result != null)
            {
                // Вызываем очистку перед возвратом
                result.Categories = CleanCategories(result.Categories);
            }

            return result;
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

    /// <summary>
    /// Очистка категорий и валидация
    /// </summary>
    /// <param name="rawCategories"></param>
    /// <returns></returns>
    private List<string> CleanCategories(List<string> rawCategories)
    {
        var cleanedList = new List<string>();

        if (rawCategories == null) return cleanedList;

        foreach (var rawCat in rawCategories)
        {
            // Пробуем найти точное совпадение
            if (_validCategories.Contains(rawCat))
            {
                cleanedList.Add(rawCat);
                continue;
            }

            // Если точного нет, пробуем очистить от скобок "(включает...)"
            // т.к. DeepSeek может вернуть: "Категория (пояснение)"
            var cleanName = rawCat.Split('(')[0].Trim();

            if (_validCategories.Contains(cleanName))
            {
                cleanedList.Add(cleanName);
            }
            else
            {
                // Если даже после очистки категория не найдена, 
                // логируем это как Warning, но не добавляем в список категорий лота.
                // Возможно, стоит добавить её в SuggestedCategory, чтобы не потерять сигнал.
                _logger.LogWarning("DeepSeek вернул несуществующую категорию: '{RawCat}' (после очистки: '{CleanName}')", rawCat, cleanName);
            }
        }

        return cleanedList.Distinct().ToList();
    }
}
