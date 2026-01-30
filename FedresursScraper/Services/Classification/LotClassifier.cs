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

namespace FedresursScraper.Services;

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

    // Переменные для Circuit Breaker
    private static DateTime _circuitOpenUntil = DateTime.MinValue;
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromHours(4); // Ждем 4 часа после ошибки оплаты

    // Минимальный интервал между запросами
    private readonly TimeSpan _minRequestInterval;
    private static DateTime _nextAllowedRequestTime = DateTime.MinValue;
    private static readonly object _lockObj = new object(); // Для синхронизации времени

    public LotClassifier(
        ILogger<LotClassifier> logger,
        IConfiguration configuration,
        string apiKey, string apiUrl)
    {
        _logger = logger;

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

    public async Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription, CancellationToken token)
    {
        // Если предохранитель сработал, не делаем запрос
        if (DateTime.UtcNow < _circuitOpenUntil)
        {
            throw new CircuitBreakerOpenException($"API недоступно до {_circuitOpenUntil}");
        }

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
            // Пример 1: Недвижимость
            new UserChatMessage(
                "Описание: Лот №1. Земельный участок для ИЖС, площадь 1500 кв.м, кадастровый номер 50:00:000000:123, расположен по адресу: Московская обл, г. Химки. На участке расположен недостроенный дом."),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Земельный участок\", \"Прочие постройки\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Земельный участок 15 сот. (ИЖС) с недостроем, Московская обл., г. Химки, КН 50:00:000000:123\",\n" +
                "  \"marketValue\": 6500000,\n" +
                "  \"investmentSummary\": \"Оценка ориентирована на цену земли под ИЖС в Химках с дисконтом за недострой и неопределённость степени готовности. Потенциал роста связан с доведением объекта до пригодного состояния и оформлением документов, но потребуются вложения и время. Риски: статус/состав недостроя, возможные обременения и расходы на коммуникации.\",\n" +
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
                "  \"title\": \"Toyota Camry, 2018 г.в., VIN X123456789 (не на ходу, требуется ремонт двигателя)\",\n" +
                "  \"marketValue\": 1500000,\n" +
                "  \"investmentSummary\": \"Рыночная стоимость рассчитана как средняя цена Camry 2018 г.в. на ходу минус дисконт на ремонт двигателя и риски скрытых дефектов. Потенциал прибыли возможен при покупке существенно ниже рынка и подтверждённой смете ремонта с быстрым выводом в продажу. Риски: неизвестный объём ремонта, юридическая чистота/ограничения и отсутствие точной диагностики.\",\n" +
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
                "  \"marketValue\": 250000,\n" +
                "  \"investmentSummary\": \"Оценка основана на том, что право требования обычно торгуется с существенным дисконтом к стоимости базового актива из‑за рисков взыскания. Потенциал прибыли появляется при успешном и быстром исполнении судебного акта (взыскание денег/получение имущества). Риски: фактическая исполнимость, сроки и расходы на взыскание, а также платёжеспособность/доступность должника.\",\n" +
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
                "  \"marketValue\": 3200000,\n" +
                "  \"investmentSummary\": \"Оценка учитывает специфику продажи доли: существенный дисконт (до 30-50%) относительно рыночной стоимости половины целой квартиры из-за сложности пользования и распоряжения. Потенциал: выкуп второй доли у сособственника или продажа всей квартиры целиком (совместно). Риски: конфликт с сособственниками, невозможность проживания, низкая ликвидность доли как самостоятельного объекта.\",\n" +
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
                "  \"marketValue\": 8500000,\n" +
                "  \"investmentSummary\": \"Оценка базируется на средней стоимости кв.м. складской недвижимости в пригороде Екатеринбурга. Инвестиционный потенциал связан с использованием под собственные нужды, сдачей в аренду или перепродажей после косметического ремонта. Риски: состояние здания, наличие/мощность коммуникаций (электричество, отопление), удобство подъездных путей для грузового транспорта.\",\n" +
                "  \"isSharedOwnership\": false,\n" +
                "  \"propertyRegionCode\": \"66\",\n" +
                "  \"propertyRegionName\": \"Свердловская область\",\n" +
                "  \"propertyFullAddress\": \"Свердловская обл., р-н Сысертский, п. Большой Исток\"\n" +
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
                "5. isSharedOwnership = true только для долевой собственности (1/2 и т.д.). Для совместно нажитой собственности isSharedOwnership = false\n" +
                "6. Проанализируй описание на наличие информации о местонахождении имущества:\n" +
                "   - Если в описании указан полный адрес (область, город, улица и т.д.), заполни propertyFullAddress, propertyRegionCode (код региона из справочника ФНС) и propertyRegionName (название региона).\n" +
                "   - Если указан только регион/область/край/республика, заполни propertyRegionCode и propertyRegionName.\n" +
                "   - Если адрес не указан, оставь эти поля null.\n" +
                "   - Коды регионов: 01-21 (республики), 22-27, 41, 59, 75 (края), 28-76 (области), 77 (Москва), 78 (Санкт-Петербург), 92 (Севастополь), 83, 86, 87, 89 (автономные округа), 79 (Еврейская АО), 99 (иные территории).\n\n" +
                "7. Определи рыночную стоимость объекта (marketValue) в рублях (число). Если стоимость указана в тексте — используй её. Если нет — дай примерную экспертную оценку для такого имущества. Если оценить невозможно, верни null.\n\n" +
                "8. Сформируй investmentSummary: 2–3 коротких предложения на русском про инвестиционную привлекательность. " +
                "Обязательно: кратко объясни, почему marketValue именно такой (ключевые факторы из описания), " +
                "упомяни 1–2 основных риска/ограничения (например: доля, обременения, состояние, неопределенность комплектации/документов), " +
                "и если уместно — возможный сценарий повышения стоимости/перепродажи. " +
                "Если данных мало — честно укажи, что оценка ориентировочная.\n\n" +

                "ФОРМАТ ОТВЕТА (JSON):\n" +
                "{ \"categories\": [], \"suggestedCategory\": null, \"title\": \"...\", \"marketValue\": null, \"investmentSummary\": null, \"isSharedOwnership\": false, \"propertyRegionCode\": null, \"propertyRegionName\": null, \"propertyFullAddress\": null }")
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
            _circuitOpenUntil = DateTime.UtcNow.Add(_cooldownPeriod);

            _logger.LogCritical(ex, "Баланс DeepSeek исчерпан (402). Запросы приостановлены на {Minutes} минут.", _cooldownPeriod.TotalMinutes);
            return null;
        }
        catch (ClientResultException ex) when (ex.Status == 429) // Too Many Requests (лимит рейтов)
        {
            // Для 429 можно паузу поменьше, например 1 минуту
            _circuitOpenUntil = DateTime.UtcNow.AddMinutes(1);
            _logger.LogWarning("Лимит запросов DeepSeek (429). Пауза 1 минута.");

            // Бросаем исключение, чтобы этот лот тоже стал Skipped
            throw new CircuitBreakerOpenException($"API Rate Limit (429) до {_circuitOpenUntil}", ex);
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
