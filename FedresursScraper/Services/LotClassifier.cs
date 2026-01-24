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

namespace FedresursScraper.Services
{
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
                Endpoint = new Uri(apiUrl)
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

        public async Task<LotClassificationResult?> ClassifyLotAsync(string lotDescription)
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

            // Пример 3: Дебиторская задолженность (права требования на авто)
            new UserChatMessage(
                "Описание: Право требования автомобиля LADA LARGUS, 2019 г.в., VIN XTAFS045LK1200566 (на основании судебного акта)"),
            new AssistantChatMessage(
                "{\n" +
                "  \"categories\": [\"Дебиторская задолженность\"],\n" +
                "  \"suggestedCategory\": null,\n" +
                "  \"title\": \"Право требования (LADA LARGUS, 2019 г.в., VIN XTAFS045LK1200566)\",\n" +
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
}
