using Azure.AI.OpenAI;
using Azure.AI.Inference;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ClientModel;
using Azure;
using System.Text.Json;
using Lots.Application.Services.DeepSeek;

namespace FedresursScraper.Services;

public class LotClassifier : ILotClassifier
{
    private readonly ILogger<LotClassifier> _logger;
    private readonly ChatClient _chatClient;
    private readonly IDeepSeekBudgetGuard _budgetGuard;
    private readonly string _modelName = "deepseek-chat";
    private readonly Dictionary<string, List<string>> _categoryTree;

    // Уточнения для конкретных категорий (Business Rules)
    private readonly Dictionary<string, string> _categoryHints;

    // Белый список чистых категорий для быстрой проверки
    private readonly HashSet<string> _validCategories;

    // Переменные для Circuit Breaker (legacy in-process throttle; глобальный breaker — в DeepSeekBudgetGuard)
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromHours(4);

    // Минимальный интервал между запросами
    private readonly TimeSpan _minRequestInterval;
    private static DateTime _nextAllowedRequestTime = DateTime.MinValue;
    private static readonly object _lockObj = new object(); // Для синхронизации времени

    private const string PropertyDescriptionInstructions =
        "0. Сначала оцени hasPropertyDescription: есть ли в тексте (и в блоке «Данные Росреестра», если он есть) " +
        "конкретная информация об имуществе (тип, адрес, площадь, марка/модель, характеристики).\n" +
        "   hasPropertyDescription = false, если текст содержит ТОЛЬКО: порядок ознакомления, контакты/телефон, " +
        "график работы, ссылки на документы — без указания, что именно продаётся.\n" +
        "   hasPropertyDescription = true, если есть хотя бы одно: тип имущества, адрес/регион, площадь, " +
        "марка/модель, оборудование, или данные Росреестра с адресом/характеристиками.\n" +
        "   Если hasPropertyDescription = false: title = null, categories = [], suggestedCategory = null, " +
        "marketValueMin/Max = null, priceConfidence = \"low\", investmentSummary = null. " +
        "НЕ придумывай title вроде «лот без описания» или «имущество не указано».\n\n";

    public LotClassifier(
        ILogger<LotClassifier> logger,
        IConfiguration configuration,
        IDeepSeekBudgetGuard budgetGuard,
        string apiKey, string apiUrl)
    {
        _logger = logger;
        _budgetGuard = budgetGuard;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(apiUrl),
            // увеличиваем таймаут сети
            NetworkTimeout = TimeSpan.FromMinutes(2), // Дефолт около 100 сек
        };

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _chatClient = openAiClient.GetChatClient(_modelName);

        double seconds = configuration.GetValue<double>("DeepSeek:RequestIntervalSeconds", 3.0);
        _minRequestInterval = TimeSpan.FromSeconds(seconds);

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
                "Легковой автомобиль", "Коммерческий транспорт и спецтехника", "Прицепы и полуприцепы, фургоны",
                "Мототехника", "Водный транспорт", "Авиатранспорт", "С/х техника", "Иной транспорт и техника"
            }},
            { "Оборудование", new List<string> {
                "Промышленное оборудование", "Строительное оборудование", "Складское оборудование",
                "Торговое оборудование", "Металлообрабатывающее оборудование", "Медицинское оборудование",
                "Пищевое оборудование", "Деревообрабатывающее оборудование", "Производственные линии",
                "Сварочное оборудование", "Насосное оборудование",
                "Измерительное оборудование, инструмент и комплектующие",
                "Оборудование и инструменты для ремонта и обслуживания транспорта",
                "Продукция химического и нефтяного машиностроения",
                "Другое оборудование"
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
            { "Коммерческий транспорт и спецтехника", "грузовики, автобусы, экскаваторы, бульдозеры, краны, погрузчики" },
            { "Прицепы и полуприцепы, фургоны", "прицепы, полуприцепы, фургоны, тентованные полуприцепы, рефрижераторы на шасси" },
            { "Насосное оборудование", "насосы, насосные станции, насосное оборудование" },
            { "Измерительное оборудование, инструмент и комплектующие", "измерительные приборы, датчики, КИПиА, измерительный инструмент" },
            { "Оборудование и инструменты для ремонта и обслуживания транспорта", "подъёмники, стенды, диагностическое оборудование, инструменты для автосервиса, шиномонтажное оборудование" },
            { "Продукция химического и нефтяного машиностроения", "оборудование для нефтегазовой и химической промышленности, резервуары, колонны, теплообменники, компрессоры" },
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

    public async Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription, CancellationToken token)
    {
        await _budgetGuard.AcquireRequestSlotAsync("classification", token);

        // THROTTLING (Ограничение скорости)
        // С использованием _nextAllowedRequestTime (алгоритм Token Bucket / Leaky Bucket в упрощенном виде):
        // 1-й запрос: пройдет сразу.
        // 2-й запрос: увидит, что слот занят, подождет 1.5 сек.
        // 3-й запрос: увидит, что занято уже на 3 сек вперед, подождет 3 сек.
        // ...
        // 20-й запрос: подождет 30 секунд.
        // Запросы выстроятся в ровную очередь с интервалом 1.5 секунды. Это гарантированно уберет 429 ошибки, вызванные пиковым RPS.
        TimeSpan delay;
        lock (_lockObj)
        {
            var now = DateTime.UtcNow;
            if (_nextAllowedRequestTime < now)
            {
                _nextAllowedRequestTime = now;
            }

            // Вычисляем, сколько нужно подождать до следующего свободного слота
            delay = _nextAllowedRequestTime - now;

            // Сдвигаем слот для следующего запроса
            _nextAllowedRequestTime = _nextAllowedRequestTime.Add(_minRequestInterval);
        }

        // Если нужно ждать, ждем (асинхронно, не блокируя поток)
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

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
            // Пример 1: Недвижимость (Дом на участке - категория Земельный участок НЕ присваивается)
            new UserChatMessage(
                "Описание: Лот №1. Земельный участок для ИЖС, площадь 1500 кв.м, кадастровый номер 50:00:000000:123, расположен по адресу: Московская обл, г. Химки. На участке расположен жилой дом площадью 120 кв.м."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Жилой дом\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Жилой дом 120 кв.м. и земельный участок 15 сот., Московская обл., г. Химки\",\n" +
                "  \"marketValueMin\": 5500000,\n" +
                "  \"marketValueMax\": 8500000,\n" +
                "  \"priceConfidence\": \"medium\",\n" +
                "  \"investmentSummary\": \"Оценочная стоимость основана на типичной цене жилых домов в Химках с учетом площади участка. Потенциал роста возможен при косметическом ремонте. Риски: фактическое состояние дома, коммуникации и возможные обременения.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": \"50\",\n" +
                "  \"propertyRegionName\": \"Московская область\",\n" +
                "  \"propertyFullAddress\": \"Московская обл, г. Химки\"\n" +
                "}"),

            // Пример 2: Транспорт
            new UserChatMessage(
                "Описание: Автомобиль легковой Toyota Camry, 2018 г.в., VIN X123456789, цвет черный, не на ходу, требуется ремонт двигателя."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Легковой автомобиль\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Toyota Camry, 2018 г.в. (не на ходу, требуется ремонт двигателя)\",\n" +
                "  \"marketValueMin\": 1200000,\n" +
                "  \"marketValueMax\": 1800000,\n" +
                "  \"priceConfidence\": \"medium\",\n" +
                "  \"investmentSummary\": \"Оценочная стоимость рассчитана как ориентир по рынку Camry 2018 г.в. минус дисконт на неходовое состояние и ремонт двигателя. Потенциал прибыли появляется при покупке ближе к нижней границе и подтверждённой смете ремонта с быстрым выходом в продажу. Риски: скрытые дефекты, объём ремонта и юридические ограничения.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": null,\n" +
                "  \"propertyRegionName\": null,\n" +
                "  \"propertyFullAddress\": null\n" +
                "}"),

            // Пример 3: Дебиторская задолженность (права требования на авто)
            new UserChatMessage(
                "Описание: Право требования автомобиля LADA LARGUS, 2019 г.в., VIN XTAFS045LK1200566 (на основании судебного акта)"),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Дебиторская задолженность\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Право требования (LADA LARGUS, 2019 г.в., VIN XTAFS045LK1200566)\",\n" +
                "  \"marketValueMin\": null,\n" +
                "  \"marketValueMax\": null,\n" +
                "  \"priceConfidence\": \"not_evaluable\",\n" +
                "  \"investmentSummary\": \"Числовая автооценка для прав требования не проводится: стоимость зависит от платёжеспособности должника, стадии взыскания и судебных расходов. Потенциал возможен при быстром исполнении, но риски высоки.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": null,\n" +
                "  \"propertyRegionName\": null,\n" +
                "  \"propertyFullAddress\": null\n" +
                "}"),

            // Пример 4: Долевая собственность
            new UserChatMessage(
                "Описание: 1/2 доля в праве общей долевой собственности на квартиру, назначение жилое, площадь 45.5 кв.м, этаж 3. Адрес: г. Санкт-Петербург, ул. Садовая, д. 10, кв. 5."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Квартира\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"1/2 доля в кв. 45.5 кв.м, г. Санкт-Петербург, ул. Садовая\",\n" +
                "  \"marketValueMin\": 2800000,\n" +
                "  \"marketValueMax\": 3500000,\n" +
                "  \"priceConfidence\": \"high\",\n" +
                "  \"investmentSummary\": \"Оценка стоимости учитывает специфику продажи доли: существенный дисконт (до 40-50%) относительно рыночной стоимости половины целой квартиры из-за сложности пользования. Потенциал: выкуп второй доли у сособственника или продажа всей квартиры целиком (совместно). Риски: конфликт с сособственниками, невозможность проживания, низкая ликвидность доли как самостоятельного объекта.\",\n" +
                "  \"isSharedOwnership\": true,\n" +
                "  \"propertyRegionCode\": \"78\",\n" +
                "  \"propertyRegionName\": \"Санкт-Петербург\",\n" +
                "  \"propertyFullAddress\": \"г. Санкт-Петербург, ул. Садовая, д. 10\"\n" +
                "}"),

            // Пример 5: Нежилое помещение (склад)
            new UserChatMessage(
                "Описание: Нежилое здание (склад), площадь 450 кв.м., кадастровый номер 66:41:0000000:777. Местонахождение: Свердловская обл., р-н Сысертский, п. Большой Исток."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Нежилое здание\", \"Складское оборудование\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Нежилое здание (склад) 450 кв.м., Свердловская обл., п. Большой Исток\",\n" +
                "  \"marketValueMin\": 7500000,\n" +
                "  \"marketValueMax\": 9500000,\n" +
                "  \"priceConfidence\": \"medium\",\n" +
                "  \"investmentSummary\": \"Оценка базируется на средней стоимости кв.м. складской недвижимости класса C в пригороде Екатеринбурга. Диапазон обусловлен неизвестным техническим состоянием. Инвестиционный потенциал: сдача в аренду или перепродажа после косметического ремонта. Риски: состояние кровли/пола, наличие/мощность коммуникаций (электричество, отопление) и удобство подъездных путей.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": \"66\",\n" +
                "  \"propertyRegionName\": \"Свердловская область\",\n" +
                "  \"propertyFullAddress\": \"Свердловская обл., р-н Сысертский, п. Большой Исток\"\n" +
                "}"),

            // Пример 6: Нет описания имущества (только порядок ознакомления)
            new UserChatMessage(
                "Описание: Лот №2. Получить дополнительную информацию об имуществе можно в рабочие дни с 11.00 до 17.00, предварительно согласовав место и дату по тел. +7 (909) 340-28-60.\nНачальная цена лота: 29631000.00 руб."),
            new AssistantChatMessage(
                "{\n" +
                "  \"hasPropertyDescription\": false,\n" +
                "  \"categories\": [],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": null,\n" +
                "  \"marketValueMin\": null,\n" +
                "  \"marketValueMax\": null,\n" +
                "  \"priceConfidence\": \"low\",\n" +
                "  \"investmentSummary\": null,\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": null,\n" +
                "  \"propertyRegionName\": null,\n" +
                "  \"propertyFullAddress\": null\n" +
                "}"),

            // Пример 7: Пакет акций (не оценивается)
            new UserChatMessage(
                "Описание: 21 000 акций (100% пакет акций) АКЦИОНЕРНОЕ ОБЩЕСТВО «КОРПУСГРУПП МАГНИТОГОРСК» ИНН: 7445034367 ОГРН 1077445001611\n" +
                "Справочно — начальная цена торгов (цена продавца, НЕ рыночная оценка): 1500000.00 руб."),
            new AssistantChatMessage(
                "{\n" +
                "  \"hasPropertyDescription\": true,\n" +
                "  \"categories\": [\"Ценные бумаги\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"100% акций АО «Корпусгрупп Магнитогорск» (21 000 шт.), ИНН 7445034367\",\n" +
                "  \"marketValueMin\": null,\n" +
                "  \"marketValueMax\": null,\n" +
                "  \"priceConfidence\": \"not_evaluable\",\n" +
                "  \"investmentSummary\": \"Числовая автооценка недоступна: для пакета акций нужны баланс эмитента, реестр акционеров и проверка обременений. Риски: скрытые долги, конфликт с действующим директором, непрозрачная структура группы.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": \"74\",\n" +
                "  \"propertyRegionName\": \"Челябинская область\",\n" +
                "  \"propertyFullAddress\": \"г. Магнитогорск\"\n" +
                "}"),

            // --- РЕАЛЬНЫЙ ЗАПРОС ---
            new UserChatMessage(
                $"Проанализируй описание лота и заполни JSON.\n\n" +
                $"ОПИСАНИЕ ЛОТА:\n{lotDescription}\n\n" +

                $"СПИСОК ДОПУСТИМЫХ КАТЕГОРИЙ:\n{categoriesPromptBuilder}\n\n" +

                "ИНСТРУКЦИИ:\n" +
                PropertyDescriptionInstructions +
                "1. Выбери категории СТРОГО из списка выше, учитывая пояснения в скобках.\n" +
                "2. Если лот подходит под несколько категорий, верни их списком. ВАЖНО: Категорию 'Земельный участок' присваивай ТОЛЬКО пустым участкам без зданий. Если на участке есть дом или здание, присваивай ТОЛЬКО категорию здания ('Жилой дом', 'Нежилое здание' и т.д.), категорию 'Земельный участок' НЕ добавляй.\n" +
                "3. Если ни одна категория не подходит, выбери 'Прочее' и заполни поле 'suggestedCategory' своим вариантом.\n" +
                "4. Сформируй название (title). Если это доля, укажи это в названии.\n" +
                "5. isSharedOwnership = true только для долевой собственности (1/2 и т.д.). Для совместно нажитой собственности isSharedOwnership = false\n" +
                "6. Проанализируй описание на наличие информации о местонахождении имущества:\n" +
                "   - Если в описании указан полный адрес (область, город, улица и т.д.), заполни propertyFullAddress, propertyRegionCode (код региона из справочника ФНС) и propertyRegionName (название региона).\n" +
                "   - Если указан только регион/область/край/республика, заполни propertyRegionCode и propertyRegionName.\n" +
                "   - Если адрес не указан, оставь эти поля null.\n" +
                "   - Коды регионов: 01-21 (республики), 22-27, 41, 59, 75 (края), 28-76 (области), 77 (Москва), 78 (Санкт-Петербург), 92 (Севастополь), 83, 86, 87, 89 (автономные округа), 79 (Еврейская АО), 99 (иные территории).\n\n" +
                LotPriceEvaluationRules.ClassificationPriceInstructions +

                "8. Сформируй investmentSummary: 2–3 предложения на русском. " +
                "Кратко объясни, какие факторы больше всего повлияли на оценочную стоимость (локация/состояние/тип/площадь/правовой статус), " +
                "упомяни 1–2 ключевых риска/ограничения и возможный сценарий прибыли (если реалистично). " +
                "Если priceConfidence = low — явно скажи, что оценка ориентировочная. " +
                "Если priceConfidence = not_evaluable — объясни, какие данные нужны для оценки, без цифр. " +
                "ВАЖНО: НЕ упоминай конкретные суммы, диапазоны цен или числовые значения стоимости в investmentSummary.\n\n" +

                "ФОРМАТ ОТВЕТА (JSON):\n" +
                "{ " +
                "\"hasPropertyDescription\": true, " +
                "\"categories\": [], " +
                "\"suggestedCategory\": null, " +
                "\"title\": \"...\", " +
                "\"marketValueMin\": null, " +
                "\"marketValueMax\": null, " +
                "\"priceConfidence\": \"low\", " +
                "\"investmentSummary\": null, " +
                "\"isSharedOwnership\": false, " +
                "\"propertyRegionCode\": null, " +
                "\"propertyRegionName\": null, " +
                "\"propertyFullAddress\": null " +
                "}")
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

            await RecordUsageAsync(response.Value.Usage, "classification", token);

            // защита от пустого ответа: DeepSeek может вернуть 200, но без содержимого
            if (response.Value.Content == null || response.Value.Content.Count == 0)
            {
                _logger.LogWarning("DeepSeek вернул пустой ответ (без контента) для лота.");
                throw new Exception("DeepSeek вернул пустой ответ (без контента)");
            }

            content = response.Value.Content.First().Text;
            _logger.LogDebug("Получен сырой ответ от DeepSeek: {Content}", content);

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
        catch (ClientResultException ex) when (ex.Status == 402) // Ошибка оплаты
        {
            await _budgetGuard.TripOnPaymentFailureAsync(token);

            _logger.LogCritical(ex, "Баланс DeepSeek исчерпан (402). Запросы приостановлены на {Minutes} минут.", _cooldownPeriod.TotalMinutes);
            return null;
        }
        catch (ClientResultException ex) when (ex.Status == 429) // Too Many Requests (лимит рейтов)
        {
            await _budgetGuard.TripOnRateLimitAsync(token);
            _logger.LogWarning("Лимит запросов DeepSeek (429). Пауза 1 минута.");

            // Бросаем исключение, чтобы этот лот тоже стал Skipped
            throw new CircuitBreakerOpenException("API Rate Limit (429)", ex);
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
        catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
        {
            // Это таймаут сети (а не отмена пользователем)
            _logger.LogError(ex, "Таймаут запроса к DeepSeek (превышен лимит времени).");
            throw new Exception("Таймаут запроса к DeepSeek (превышен лимит времени)");
        }
    }

    public async Task<Dictionary<Guid, LotClassificationResult>> ClassifyLotsBatchAsync(
        Dictionary<Guid, string> lotDescriptions, 
        CancellationToken token)
    {
        if (lotDescriptions == null || lotDescriptions.Count == 0)
        {
            return new Dictionary<Guid, LotClassificationResult>();
        }

        // Если только один лот, используем обычный метод для совместимости
        if (lotDescriptions.Count == 1)
        {
            var singleLot = lotDescriptions.First();
            var result = await ClassifyLotAsync(singleLot.Value, token);
            if (result != null)
            {
                return new Dictionary<Guid, LotClassificationResult> { { singleLot.Key, result } };
            }
            return new Dictionary<Guid, LotClassificationResult>();
        }

        // Если предохранитель сработал, не делаем запрос
        await _budgetGuard.AcquireRequestSlotAsync("classification-batch", token);

        // THROTTLING (Ограничение скорости)
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

        // Формируем список категорий с подсказками
        var categoriesPromptBuilder = new System.Text.StringBuilder();
        foreach (var group in _categoryTree)
        {
            categoriesPromptBuilder.AppendLine($"- Группа '{group.Key}':");
            foreach (var category in group.Value)
            {
                string line = _categoryHints.TryGetValue(category, out var hint)
                    ? $"  * {category} (включает: {hint})"
                    : $"  * {category}";
                categoriesPromptBuilder.AppendLine(line);
            }
        }

        // Формируем список лотов для промпта
        var lotsListBuilder = new System.Text.StringBuilder();
        var lotIds = lotDescriptions.Keys.ToList();
        for (int i = 0; i < lotIds.Count; i++)
        {
            lotsListBuilder.AppendLine($"ЛОТ {i + 1} (ID: {lotIds[i]}):");
            lotsListBuilder.AppendLine(lotDescriptions[lotIds[i]]);
            lotsListBuilder.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Ты — эксперт по анализу имущества на торгах по банкротству."),

            // Few-shot примеры (те же, что и для одиночной классификации)
            new UserChatMessage(
                "Описание: Лот №1. Земельный участок для ИЖС, площадь 1500 кв.м, кадастровый номер 50:00:000000:123, расположен по адресу: Московская обл, г. Химки. На участке расположен жилой дом площадью 120 кв.м."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Жилой дом\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Жилой дом 120 кв.м. и земельный участок 15 сот., Московская обл., г. Химки\",\n" +
                "  \"marketValueMin\": 5500000,\n" +
                "  \"marketValueMax\": 8500000,\n" +
                "  \"priceConfidence\": \"medium\",\n" +
                "  \"investmentSummary\": \"Оценочная стоимость основана на типичной цене жилых домов в Химках с учетом площади участка. Потенциал роста возможен при косметическом ремонте. Риски: фактическое состояние дома, коммуникации и возможные обременения.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": \"50\",\n" +
                "  \"propertyRegionName\": \"Московская область\",\n" +
                "  \"propertyFullAddress\": \"Московская обл, г. Химки\"\n" +
                "}"),

            // Пример: нет описания имущества
            new UserChatMessage(
                "ЛОТ 99 (ID: 00000000-0000-0000-0000-000000000099):\n" +
                "Лот №2. Получить дополнительную информацию об имуществе можно в рабочие дни с 11.00 до 17.00, предварительно согласовав место и дату по тел. +7 (909) 340-28-60.\n" +
                "Начальная цена лота: 29631000.00 руб.\n"),
            new AssistantChatMessage(
                "{\n" +
                "  \"00000000-0000-0000-0000-000000000099\": {\n" +
                "    \"hasPropertyDescription\": false,\n" +
                "    \"categories\": [],\n" +
                "    \"suggestedCategory\": null,\n" +
                "    \"title\": null,\n" +
                "    \"marketValueMin\": null,\n" +
                "    \"marketValueMax\": null,\n" +
                "    \"priceConfidence\": \"low\",\n" +
                "    \"investmentSummary\": null,\n" +
                "    \"isSharedOwnership\": false,\n" +
                "    \"propertyRegionCode\": null,\n" +
                "    \"propertyRegionName\": null,\n" +
                "    \"propertyFullAddress\": null\n" +
                "  }\n" +
                "}"),

            // РЕАЛЬНЫЙ ЗАПРОС для батча
            new UserChatMessage(
                $"Проанализируй описания {lotIds.Count} лотов и заполни JSON для каждого.\n\n" +
                $"ОПИСАНИЯ ЛОТОВ:\n{lotsListBuilder}\n" +
                $"СПИСОК ДОПУСТИМЫХ КАТЕГОРИЙ:\n{categoriesPromptBuilder}\n\n" +
                "ИНСТРУКЦИИ:\n" +
                PropertyDescriptionInstructions +
                "1. Для каждого лота выбери категории СТРОГО из списка выше, учитывая пояснения в скобках.\n" +
                "2. Если лот подходит под несколько категорий, верни их списком. ВАЖНО: Категорию 'Земельный участок' присваивай ТОЛЬКО пустым участкам без зданий. Если на участке есть дом или здание, присваивай ТОЛЬКО категорию здания ('Жилой дом', 'Нежилое здание' и т.д.), категорию 'Земельный участок' НЕ добавляй.\n" +
                "3. Если ни одна категория не подходит, выбери 'Прочее' и заполни поле 'suggestedCategory' своим вариантом.\n" +
                "4. Сформируй название (title) для каждого лота. Если это доля, укажи это в названии.\n" +
                "5. isSharedOwnership = true только для долевой собственности (1/2 и т.д.). Для совместно нажитой собственности isSharedOwnership = false\n" +
                "6. Проанализируй описание на наличие информации о местонахождении имущества:\n" +
                "   - Если в описании указан полный адрес (область, город, улица и т.д.), заполни propertyFullAddress, propertyRegionCode (код региона из справочника ФНС) и propertyRegionName (название региона).\n" +
                "   - Если указан только регион/область/край/республика, заполни propertyRegionCode и propertyRegionName.\n" +
                "   - Если адрес не указан, оставь эти поля null.\n" +
                "   - Коды регионов: 01-21 (республики), 22-27, 41, 59, 75 (края), 28-76 (области), 77 (Москва), 78 (Санкт-Петербург), 92 (Севастополь), 83, 86, 87, 89 (автономные округа), 79 (Еврейская АО), 99 (иные территории).\n\n" +
                LotPriceEvaluationRules.ClassificationPriceInstructions +
                "8. Сформируй investmentSummary: 2–3 предложения на русском. " +
                "Кратко объясни, какие факторы больше всего повлияли на оценочную стоимость (локация/состояние/тип/площадь/правовой статус), " +
                "упомяни 1–2 ключевых риска/ограничения и возможный сценарий прибыли (если реалистично). " +
                "Если priceConfidence = low — явно скажи, что оценка ориентировочная. " +
                "Если priceConfidence = not_evaluable — объясни, какие данные нужны для оценки, без цифр. " +
                "ВАЖНО: НЕ упоминай конкретные суммы, диапазоны цен или числовые значения стоимости в investmentSummary.\n\n" +
                "ФОРМАТ ОТВЕТА (JSON объект, где ключи - это ID лотов в формате строки):\n" +
                "{\n" +
                string.Join(",\n", lotIds.Select(id => $"  \"{id}\": {{ \"hasPropertyDescription\": true, \"categories\": [], \"suggestedCategory\": null, \"title\": \"...\", \"marketValueMin\": null, \"marketValueMax\": null, \"priceConfidence\": \"low\", \"investmentSummary\": null, \"isSharedOwnership\": false, \"propertyRegionCode\": null, \"propertyRegionName\": null, \"propertyFullAddress\": null }}")) +
                "\n}\n\n" +
                $"ВАЖНО: Верни JSON объект с {lotIds.Count} элементами, где каждый ключ - это ID лота (Guid в виде строки: \"{lotIds[0]}\"), а значение - результат классификации для этого лота.")
        };

        var chatCompletionOptions = new ChatCompletionOptions()
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        string content = string.Empty;
        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, chatCompletionOptions);

            await RecordUsageAsync(response.Value.Usage, "classification-batch", token);

            if (response.Value.Content == null || response.Value.Content.Count == 0)
            {
                _logger.LogWarning("DeepSeek вернул пустой ответ (без контента) для батча лотов.");
                throw new Exception("DeepSeek вернул пустой ответ (без контента)");
            }

            content = response.Value.Content.First().Text;
            _logger.LogDebug("Получен сырой ответ от DeepSeek для батча: {Content}", content);

            content = content.Trim().Replace("```json", "").Trim().Replace("```", "").Trim();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            var batchResult = JsonSerializer.Deserialize<Dictionary<string, LotClassificationResult>>(content, options);

            if (batchResult == null)
            {
                _logger.LogWarning("Не удалось десериализовать батч результатов классификации.");
                return new Dictionary<Guid, LotClassificationResult>();
            }

            var result = new Dictionary<Guid, LotClassificationResult>();
            foreach (var kvp in batchResult)
            {
                if (Guid.TryParse(kvp.Key, out var lotId) && kvp.Value != null)
                {
                    // Очищаем категории
                    kvp.Value.Categories = CleanCategories(kvp.Value.Categories);
                    result[lotId] = kvp.Value;
                }
                else
                {
                    _logger.LogWarning("Не удалось распарсить ID лота или результат классификации: Key={Key}", kvp.Key);
                }
            }

            _logger.LogInformation("Успешно классифицировано {Count} из {Total} лотов в батче.", result.Count, lotIds.Count);
            return result;
        }
        catch (ClientResultException ex) when (ex.Status == 402)
        {
            await _budgetGuard.TripOnPaymentFailureAsync(token);
            _logger.LogCritical(ex, "Баланс DeepSeek исчерпан (402). Запросы приостановлены на {Minutes} минут.", _cooldownPeriod.TotalMinutes);
            throw new CircuitBreakerOpenException("Баланс DeepSeek исчерпан (402)", ex);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            await _budgetGuard.TripOnRateLimitAsync(token);
            _logger.LogWarning("Лимит запросов DeepSeek (429). Пауза 1 минута.");
            throw new CircuitBreakerOpenException("API Rate Limit (429)", ex);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogCritical(jsonEx, "Ошибка десериализации JSON батча. Контент после очистки: {Content}", content);
            return new Dictionary<Guid, LotClassificationResult>();
        }
        catch (ClientResultException ex)
        {
            _logger.LogCritical(ex, "Произошла ошибка при вызове API DeepSeek для батча");
            return new Dictionary<Guid, LotClassificationResult>();
        }
        catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
        {
            _logger.LogError(ex, "Таймаут запроса к DeepSeek для батча (превышен лимит времени).");
            throw new Exception("Таймаут запроса к DeepSeek для батча (превышен лимит времени)");
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

    private async Task RecordUsageAsync(OpenAI.Chat.ChatTokenUsage? usage, string caller, CancellationToken token)
    {
        if (usage?.TotalTokenCount is > 0)
        {
            await _budgetGuard.RecordTokenUsageAsync(caller, usage.TotalTokenCount, token);
        }
    }
}
