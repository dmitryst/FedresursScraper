using HtmlAgilityPack;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FedresursScraper.Services
{
    public interface IMetsEnrichmentService
    {
        Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct);

        Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct);
    }

    public class MetsEnrichmentService : IMetsEnrichmentService
    {
        private readonly LotsDbContext _context;
        private readonly IFileStorageService _fileStorage;
        private readonly HttpClient _httpClient;
        private readonly ILogger<MetsEnrichmentService> _logger;

        private const int BatchSize = 5;
        private const int MaxRetryCount = 3;
        
        // Минимальная задержка перед первым обогащением (в часах)
        // Фото на сайте площадки могут появиться в течение нескольких часов после создания лота
        private const int MinHoursBeforeFirstEnrichment = 3;
        
        // Задержка для повторных попыток обогащения лотов без фото (в часах)
        private const int RetryDelayHoursForMissingImages = 4;
        
        // Максимальное количество попыток обогащения для лотов без фото
        // Если фото не найдено N раз подряд, помечаем лот как обогащенный (даже без фото)
        private const int MaxMissingImagesAttempts = 3;

        public MetsEnrichmentService(
            LotsDbContext context,
            IFileStorageService fileStorage,
            HttpClient httpClient,
            ILogger<MetsEnrichmentService> logger)
        {
            _context = context;
            _fileStorage = fileStorage;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct)
        {
            // Дата отсечения (09.01.2026)
            // Используем UTC, так как в базе хранится UTC
            var dateThreshold = new DateTime(2026, 1, 9, 0, 0, 0, DateTimeKind.Utc);
            
            // Минимальное время с момента создания лота до первого обогащения
            var minEnrichmentTime = DateTime.UtcNow.AddHours(-MinHoursBeforeFirstEnrichment);
            
            // Время для повторных попыток (если фото не было найдено ранее)
            var retryTimeThreshold = DateTime.UtcNow.AddHours(-RetryDelayHoursForMissingImages);

            // Выбираем торги МЭТС, которые еще не были обработаны
            var biddings = await _context.Biddings
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Images)
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Documents)
                .Include(b => b.Lots)
                    .ThenInclude(l => l.PriceSchedules)
                .Include(b => b.EnrichmentState)
                .Where(b => b.Platform.Contains("Межрегиональная Электронная Торговая Система"))
                .Where(b => !b.IsEnriched ?? true)
                .Where(b => b.EnrichmentState == null || 
                    (b.EnrichmentState.RetryCount < MaxRetryCount && 
                     b.EnrichmentState.MissingImagesAttemptCount < MaxMissingImagesAttempts))
                .Where(b => b.CreatedAt > dateThreshold)
                // Фильтр по времени: либо лот создан достаточно давно для первого обогащения,
                // либо прошло достаточно времени с последней попытки для повторного обогащения
                .Where(b => 
                    (b.EnrichmentState == null && b.CreatedAt <= minEnrichmentTime) || // Первое обогащение
                    (b.EnrichmentState != null && b.EnrichmentState.LastAttemptAt.HasValue && 
                     b.EnrichmentState.LastAttemptAt <= retryTimeThreshold) || // Повторная попытка
                    (b.EnrichmentState != null && !b.EnrichmentState.LastAttemptAt.HasValue && 
                     b.CreatedAt <= minEnrichmentTime)) // Первая попытка после создания EnrichmentState
                // дебаг
                //.Where(b => b.Id == Guid.Parse("32d7c1b5-e39d-41df-b520-ca2f317594e5"))
                .OrderByDescending(b => b.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (!biddings.Any())
                return false;

            foreach (var bidding in biddings)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var hasImages = await EnrichBiddingAsync(bidding, ct);

                    // Помечаем как обогащенное только если найдены фото
                    // Если фото нет, оставляем IsEnriched = false для повторной попытки позже
                    if (hasImages)
                    {
                        bidding.IsEnriched = true;
                        bidding.EnrichedAt = DateTime.UtcNow;
                        
                        // Сбрасываем состояние ошибок и счетчик попыток без фото, если были
                        if (bidding.EnrichmentState != null)
                        {
                            bidding.EnrichmentState.LastError = null;
                            bidding.EnrichmentState.MissingImagesAttemptCount = 0; // Сбрасываем счетчик, так как фото найдены
                        }
                        
                        _logger.LogInformation("Успешно обогащены торги {TradeNumber} (найдены фото)", bidding.TradeNumber);
                    }
                    else
                    {
                        // Фото не найдено - создаем или обновляем EnrichmentState для повторной попытки
                        if (bidding.EnrichmentState == null)
                        {
                            bidding.EnrichmentState = new EnrichmentState
                            {
                                BiddingId = bidding.Id,
                                RetryCount = 0, // Не считаем это ошибкой
                                MissingImagesAttemptCount = 1, // Первая попытка без фото
                                LastAttemptAt = DateTime.UtcNow,
                                LastError = "Фото не найдено на сайте площадки. Повторная попытка будет позже."
                            };
                        }
                        else
                        {
                            bidding.EnrichmentState.MissingImagesAttemptCount++;
                            bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
                            bidding.EnrichmentState.LastError = "Фото не найдено на сайте площадки. Повторная попытка будет позже.";
                        }
                        
                        // Если достигли максимума попыток без фото, помечаем как обогащенный
                        // (для некоторых лотов фото может вообще не быть на сайте)
                        if (bidding.EnrichmentState.MissingImagesAttemptCount >= MaxMissingImagesAttempts)
                        {
                            bidding.IsEnriched = true;
                            bidding.EnrichedAt = DateTime.UtcNow;
                            
                            _logger.LogInformation(
                                "Торги {TradeNumber} помечены как обогащенные без фото (попыток: {Attempts}/{Max}). " +
                                "Фото, вероятно, отсутствуют на сайте площадки.",
                                bidding.TradeNumber, 
                                bidding.EnrichmentState.MissingImagesAttemptCount,
                                MaxMissingImagesAttempts);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Торги {TradeNumber} обогащены частично (фото не найдено). " +
                                "Попытка {Attempt}/{Max}. Повторная попытка через {Hours} часов.", 
                                bidding.TradeNumber,
                                bidding.EnrichmentState.MissingImagesAttemptCount,
                                MaxMissingImagesAttempts,
                                RetryDelayHoursForMissingImages);
                        }
                    }

                    // Сохраняем прогресс после каждого успешного лота (или пачки)
                    // Можно вынести SaveChanges за цикл для скорости, но внутри надежнее
                    await _context.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    // Работаем с состоянием ошибки
                    if (bidding.EnrichmentState == null)
                    {
                        // Создаем запись, если это первая ошибка
                        bidding.EnrichmentState = new EnrichmentState
                        {
                            BiddingId = bidding.Id,
                            RetryCount = 1,
                            LastAttemptAt = DateTime.UtcNow,
                            LastError = ex.Message
                        };
                    }
                    else
                    {
                        // Обновляем существующую
                        bidding.EnrichmentState.RetryCount++;
                        bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
                        bidding.EnrichmentState.LastError = ex.Message;
                    }

                    await _context.SaveChangesAsync(ct);

                    _logger.LogError(ex,
                        "Ошибка при обогащении торгов {TradeNumber}. Попытка {Retry}/{Max}.",
                        bidding.TradeNumber,
                        bidding.EnrichmentState.RetryCount,
                        MaxRetryCount);
                }
            }

            return true;
        }

        public async Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct)
        {
            var bidding = await _context.Biddings
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Images)
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Documents)
                .Include(b => b.Lots)
                    .ThenInclude(l => l.PriceSchedules)
                .Include(b => b.EnrichmentState)
                .FirstOrDefaultAsync(b => b.TradeNumber == tradeNumber || b.TradeNumber.StartsWith(tradeNumber), ct);

            if (bidding == null)
            {
                throw new KeyNotFoundException($"Торги с номером {tradeNumber} не найдены в базе.");
            }

            var hasImages = await EnrichBiddingAsync(bidding, ct);

            // При ручном обогащении помечаем как обогащенное независимо от наличия фото
            // (пользователь может захотеть обогатить даже если фото еще нет)
            bidding.IsEnriched = true;
            bidding.EnrichedAt = DateTime.UtcNow;

            // Сбрасываем ошибки, если были, так как мы запустили вручную и преуспели
            if (bidding.EnrichmentState != null)
            {
                bidding.EnrichmentState.LastError = null;
            }

            await _context.SaveChangesAsync(ct);

            if (hasImages)
            {
                _logger.LogInformation("Ручное обогащение торгов {TradeNumber} выполнено успешно (найдены фото).", tradeNumber);
            }
            else
            {
                _logger.LogWarning("Ручное обогащение торгов {TradeNumber} выполнено, но фото не найдено.", tradeNumber);
            }
        }

        private async Task<bool> EnrichBiddingAsync(Bidding bidding, CancellationToken ct)
        {
            var url = GetMetsUrl(bidding.TradeNumber);

            // Скачиваем HTML
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Не удалось получить страницу {url}. Status: {response.StatusCode}");
            }

            var htmlContent = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            bool hasAnyImages = false;

            foreach (var lot in bidding.Lots)
            {
                // Очищаем старые данные перед парсингом, чтобы избежать дубликатов
                // Так как мы сделали Include, EF удалит старые записи из БД при SaveChanges
                lot.Images.Clear();
                lot.Documents.Clear();
                lot.PriceSchedules.Clear();

                // Парсинг и сохранение картинок
                var hasImages = await ProcessImagesAsync(lot, doc, ct);
                if (hasImages)
                {
                    hasAnyImages = true;
                }

                // Парсинг графика цены (только для Публичного предложения)
                // Выполняем даже если фото нет - это полезная информация
                if (IsPublicOffer(bidding.Type))
                {
                    ProcessPriceSchedule(lot, doc);
                }
            }

            return hasAnyImages;
        }

        private string GetMetsUrl(string tradeNumber)
        {
            // Логика: "190006-МЭТС-1" -> "190006-1"
            // Ссылка: https://m-ets.ru/190006-1
            var cleanNumber = tradeNumber.Replace("-МЭТС", "");
            return $"https://m-ets.ru/{cleanNumber}";
        }

        private bool IsPublicOffer(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type.Contains("Публичное", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ProcessImagesAsync(Lot lot, HtmlDocument doc, CancellationToken ct)
        {
            // Передаем lot, чтобы сформировать точный поисковый запрос
            var imageUrls = ExtractImageUrls(doc, lot);

            if (!imageUrls.Any())
            {
                _logger.LogWarning("Lot {LotNumber}: Images not found using strict scoped search.", lot.LotNumber);
                return false;
            }

            int order = 0;
            int successCount = 0;
            foreach (var imgUrl in imageUrls)
            {
                try
                {
                    var imgBytes = await _httpClient.GetByteArrayAsync(imgUrl, ct);
                    var fileName = $"lots/{lot.Id}/{Guid.NewGuid()}.jpg";
                    var s3Url = await _fileStorage.UploadAsync(imgBytes, fileName);

                    lot.Images.Add(new LotImage
                    {
                        LotId = lot.Id,
                        Url = s3Url,
                        Order = order++
                    });
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Не удалось скачать картинку {Url}: {Message}", imgUrl, ex.Message);
                }
            }

            return successCount > 0;
        }

        private List<string> ExtractImageUrls(HtmlDocument doc, Lot lot)
        {
            var urls = new HashSet<string>();

            var searchId = $"{lot.Bidding.TradeNumber}";

            // СТРОГИЙ ПОИСК:
            // 1. Ищем span, содержащий ID лота (contains справляется с пробелами " 190009-МЭТС-39 ")
            // 2. Поднимаемся к общему контейнеру лота (general-block)
            // 3. Внутри контейнера ищем блок галереи и ссылки на скачивание
            var xpath = $"//span[@lot-id-num and contains(., '{searchId}')]/ancestor::div[contains(@class, 'general-block')]//div[contains(@class, 'gallery-container')]//a[contains(@href, 'download/')]";

            var nodes = doc.DocumentNode.SelectNodes(xpath);

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var href = node.GetAttributeValue("href", "");

                    // Фильтрация расширений и исключение превью
                    if ((href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         href.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                         href.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) &&
                        !href.Contains("_thumb_"))
                    {
                        // Приведение к абсолютному URL
                        if (!href.StartsWith("http"))
                        {
                            var path = href.StartsWith("/") ? href : "/" + href;
                            href = "https://m-ets.ru" + path;
                        }
                        urls.Add(href);
                    }
                }
            }

            return urls.ToList();
        }

        private void ProcessPriceSchedule(Lot lot, HtmlDocument doc)
        {
            // Если начальной цены нет в базе, мы не можем валидировать таблицу.
            // Тут решайте сами: либо пропускать, либо брать первую попавшуюся (риск ошибки).
            if (lot.StartPrice <= 0)
            {
                _logger.LogWarning("Lot {LotNumber}: StartPrice is 0 or empty. Cannot validate price schedule table accurately.", lot.LotNumber);
                return;
            }

            // Ищем кандидатов: таблицы с заголовками "Дата начала" и "Цена"
            var candidateTables = doc.DocumentNode.SelectNodes(
                "//table[.//th[contains(., 'Дата начала')] and .//th[contains(., 'Цена')]]"
            );

            if (candidateTables == null || !candidateTables.Any())
            {
                _logger.LogWarning("Lot {LotNumber}: No candidate tables found.", lot.LotNumber);
                return;
            }

            HtmlNode targetTable = null;

            foreach (var table in candidateTables)
            {
                // Пропускаем явно скрытые (оптимизация)
                if (IsNodeHiddenSimple(table)) continue;

                // Берем первую строку с данными (пропуская заголовок)
                var firstRow = table.SelectNodes(".//tr")?.Skip(1).FirstOrDefault();
                if (firstRow == null) continue;

                var cols = firstRow.SelectNodes("td");
                if (cols == null || cols.Count < 4) continue;

                // Пытаемся достать цену из 4-й колонки (индекс 3), как обычно на МЭТС
                // Для надежности чистим текст от всего лишнего
                if (TryParsePrice(cols[3].InnerText, out decimal sitePrice))
                {
                    // СТРОГОЕ СРАВНЕНИЕ
                    if (sitePrice == lot.StartPrice)
                    {
                        targetTable = table;
                        _logger.LogInformation("Lot {LotNumber}: Table MATCHED! DB: {P1} == Site: {P2}", lot.LotNumber, lot.StartPrice, sitePrice);
                        break; // Нашли! Выходим из цикла.
                    }
                    else
                    {
                        // Логируем для отладки, почему не подошло (полезно, если форматы разные)
                        _logger.LogDebug("Lot {LotNumber}: Table mismatch. DB: {P1} != Site: {P2}", lot.LotNumber, lot.StartPrice, sitePrice);
                    }
                }
            }

            if (targetTable == null)
            {
                _logger.LogError("Lot {LotNumber}: CRITICAL. Could not find price schedule matching StartPrice {Price}. Aborting schedule parsing.", lot.LotNumber, lot.StartPrice);
                return;
            }

            // Парсим найденную таблицу
            ParseTableRows(lot, targetTable);
        }

        // Упрощенная проверка скрытости (только ближайшие родители и явный display:none)
        private bool IsNodeHiddenSimple(HtmlNode node)
        {
            var current = node;
            // Проверяем 5 уровней вверх, чтобы не уйти в body
            for (int i = 0; i < 5; i++)
            {
                if (current == null) break;
                var style = current.GetAttributeValue("style", "").ToLower();
                if (style.Contains("display:none") || style.Contains("display: none")) return true;
                current = current.ParentNode;
            }
            return false;
        }

        // Хелпер для парсинга цены
        private bool TryParsePrice(string rawText, out decimal price)
        {
            // 1. Убираем HTML-пробелы (&nbsp;) и обычные пробелы
            // 2. Убираем "руб", "руб." и прочий мусор
            // 3. Заменяем точку на запятую (если вдруг культура требует), или наоборот, 
            // но лучше использовать CultureInfo("ru-RU") где разделитель - запятая.

            var clean = rawText
                .Replace("&nbsp;", "")
                .Replace('\u00A0', ' ') // Non-breaking space char
                .Replace(" ", "")
                .Replace("руб.", "")
                .Replace("руб", "")
                .Trim();

            return decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("ru-RU"), out price);
        }

        private void ParseTableRows(Lot lot, HtmlNode table)
        {
            var rows = table.SelectNodes(".//tr");
            if (rows == null) return;

            // Желательно очистить коллекцию перед добавлением, 
            // чтобы при повторном запуске не дублировать данные.
            // Если коллекция подгружена из БД с трекингом, это удалит старые записи.
            lot.PriceSchedules.Clear();

            // Пропускаем заголовок (первую строку)
            foreach (var row in rows.Skip(1))
            {
                var cols = row.SelectNodes("td");

                // Ожидаем минимум 4 колонки: №, Нач.Дата, Кон.Дата, Цена
                if (cols == null || cols.Count < 4) continue;

                // Индексы колонок (стандарт МЭТС):
                // [0] - Номер этапа
                // [1] - Дата начала
                // [2] - Дата окончания
                // [3] - Цена
                // [4] - Задаток (опционально)

                if (DateTime.TryParse(CleanText(cols[1].InnerText), new CultureInfo("ru-RU"), DateTimeStyles.None, out var startLocal) &&
                    DateTime.TryParse(CleanText(cols[2].InnerText), new CultureInfo("ru-RU"), DateTimeStyles.None, out var endLocal) &&
                    TryParsePrice(cols[3].InnerText, out var price))
                {
                    // Приводим даты к UTC для корректного сохранения в Postgres (timestamp with time zone)
                    // Если считаем, что на сайте МСК, то можно использовать TimeZoneInfo.ConvertTime
                    // Но чаще достаточно просто пометить как UTC, если время уже "похоже" на правду.
                    var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);
                    var endUtc = DateTime.SpecifyKind(endLocal, DateTimeKind.Utc);

                    decimal deposit = 0;
                    // Проверяем, есть ли 5-я колонка с задатком
                    if (cols.Count > 4)
                    {
                        TryParsePrice(cols[4].InnerText, out deposit);
                    }

                    lot.PriceSchedules.Add(new LotPriceSchedule
                    {
                        // ID генерируется базой или конструктором, если Guid
                        LotId = lot.Id,
                        StartDate = startUtc,
                        EndDate = endUtc,
                        Price = price,
                        Deposit = deposit
                    });
                }
                else
                {
                    // Лог warning, если строка таблицы не распарсилась (например, изменился формат)
                    // _logger.LogWarning("Failed to parse row for Lot {LotNumber}: {RowHtml}", lot.LotNumber, row.OuterHtml);
                }
            }
        }

        // Вспомогательный метод для очистки текста (включая "склеенные" даты)
        private string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            // 1. Убираем HTML-сущности и лишние пробелы
            var text = input
                .Replace("&nbsp;", " ")
                .Replace('\u00A0', ' ')
                .Trim();

            // 2. Хак для бага МЭТС: иногда дата дублируется без пробела
            // "09.01.2026 09:0009.01.26 09:00" -> берем первые 16 символов
            // Формат "dd.MM.yyyy HH:mm" занимает ровно 16 символов.
            if (text.Length > 20 && char.IsDigit(text[0]) && text.Contains(":"))
            {
                // Дополнительная проверка, чтобы не обрезать длинные комментарии, если они вдруг попадут сюда
                return text.Substring(0, 16);
            }

            return text;
        }
    }
}
