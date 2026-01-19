using HtmlAgilityPack;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace FedresursScraper.Services
{
    public interface ICdtEnrichmentService
    {
        Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct);

        Task EnrichByTradeNumberAsync(string tradeNumber, CancellationToken ct);
    }

    public class CdtEnrichmentService : ICdtEnrichmentService
    {
        private readonly LotsDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IFileStorageService _fileStorage;
        private readonly ILogger<CdtEnrichmentService> _logger;

        // Настройки батчей
        private const int BatchSize = 5;
        private const int MaxRetryCount = 3;

        public CdtEnrichmentService(
            LotsDbContext context,
            HttpClient httpClient,
            IFileStorageService fileStorage,
            ILogger<CdtEnrichmentService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _fileStorage = fileStorage;
            _logger = logger;
        }

        public async Task<bool> ProcessPendingBiddingsAsync(CancellationToken ct)
        {
            // Дата отсечения (15.01.2026)
            // Используем UTC, так как в базе хранится UTC
            var dateThreshold = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

            // Берем торги ЦДТ, которые еще не обогащены
            var biddings = await _context.Biddings
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Images)
                .Include(b => b.Lots)
                    .ThenInclude(l => l.Documents)
                .Include(b => b.EnrichmentState)
                .Where(b => b.Platform.Contains("Центр дистанционных торгов"))
                .Where(b => !b.IsEnriched ?? true)
                .Where(b => b.EnrichmentState == null || b.EnrichmentState.RetryCount < MaxRetryCount)
                .Where(b => b.CreatedAt > dateThreshold)
                .OrderByDescending(b => b.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (!biddings.Any())
            {
                return false;
            }

            foreach (var bidding in biddings)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await EnrichBiddingAsync(bidding, ct);

                    bidding.IsEnriched = true;
                    bidding.EnrichedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync(ct);

                    _logger.LogInformation("Успешно обогащены торги ЦДТ {TradeNumber}", bidding.TradeNumber);
                }
                catch (Exception ex)
                {
                    HandleError(bidding, ex);

                    await _context.SaveChangesAsync(ct);

                    _logger.LogError(ex, "Ошибка при обогащении торгов ЦДТ {TradeNumber}", bidding.TradeNumber);
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
                .Include(b => b.EnrichmentState)
                .FirstOrDefaultAsync(b => b.TradeNumber == tradeNumber || b.TradeNumber.StartsWith(tradeNumber), ct);

            if (bidding == null)
            {
                throw new KeyNotFoundException($"Торги с номером {tradeNumber} не найдены в базе.");
            }

            await EnrichBiddingAsync(bidding, ct);

            // Обновляем статус и сохраняем
            bidding.IsEnriched = true;
            bidding.EnrichedAt = DateTime.UtcNow;

            // Сбрасываем ошибки, если были, так как мы запустили вручную и преуспели
            if (bidding.EnrichmentState != null)
            {
                bidding.EnrichmentState.LastError = null;
            }

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Ручное обогащение торгов {TradeNumber} выполнено успешно.", tradeNumber);
        }


        private async Task EnrichBiddingAsync(Bidding bidding, CancellationToken ct)
        {
            // Формируем URL
            // Пример: https://bankrot.cdtrf.ru/public/undef/card/trade.aspx?id=321404
            var tradeId = CleanTradeNumber(bidding.TradeNumber);
            var url = $"https://bankrot.cdtrf.ru/public/undef/card/trade.aspx?id={tradeId}";

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Не удалось получить страницу {url}. Status: {response.StatusCode}");
            }

            var htmlContent = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            foreach (var lot in bidding.Lots)
            {
                // Очищаем старые данные перед парсингом, чтобы избежать дубликатов
                // Так как мы сделали Include, EF удалит старые записи из БД при SaveChanges
                lot.Images.Clear();
                lot.Documents.Clear();

                // Парсинг фото и документов
                await ProcessAttachmentsAsync(lot, doc, ct);

                // Парсинг графика снижения цены
                if (IsPublicOffer(bidding.Type))
                {
                    ProcessPriceSchedule(lot, doc);
                }
            }
        }

        private async Task ProcessAttachmentsAsync(Lot lot, HtmlDocument doc, CancellationToken ct)
        {
            // Ищем все ссылки на документы (pdoc.aspx)
            // Пример: <a href="/public/undef/card/pdoc.aspx?tradeid=321544&id=...">план бассеин баня</a>
            var pdocLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, 'pdoc.aspx')]");

            if (pdocLinks == null || !pdocLinks.Any())
            {
                _logger.LogInformation("Lot {LotNumber}: Документы/фото не найдены.", lot.LotNumber);
                return;
            }

            int imgOrder = 0;

            // HashSet для исключения дубликатов ссылок на скачивание
            var processedUrls = new HashSet<string>();

            foreach (var linkNode in pdocLinks)
            {
                var title = linkNode.InnerText.Trim();
                var pdocUrl = linkNode.GetAttributeValue("href", "");

                // Пропускаем стандартные документы, если будет нужно
                // if (title.Contains("Договор")) continue; 

                if (string.IsNullOrEmpty(pdocUrl)) continue;

                // Приводим к абсолютному URL
                if (!pdocUrl.StartsWith("http"))
                {
                    pdocUrl = "https://bankrot.cdtrf.ru/public/undef/card" + (pdocUrl.StartsWith("/") ? pdocUrl : "/" + pdocUrl);
                }

                try
                {
                    // Вызываем метод и получаем true, если это была картинка (чтобы увеличить счетчик)
                    bool isImageAdded = await ProcessSingleDocumentPageAsync(lot, pdocUrl, title, processedUrls, imgOrder, ct);

                    if (isImageAdded)
                    {
                        imgOrder++; // Увеличиваем счетчик только если добавили картинку
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Lot {LotNumber}: Ошибка при обработке документа '{Title}': {Error}", lot.LotNumber, title, ex.Message);
                }
            }
        }

        // Возвращает true, если был добавлен файл и это была КАРТИНКА (нужно инкрементировать order)
        private async Task<bool> ProcessSingleDocumentPageAsync(
            Lot lot,
            string pdocUrl,
            string title,
            HashSet<string> processedUrls,
            int currentImgOrder,
            CancellationToken ct)
        {
            // Небольшая пауза, чтобы не спамить запросами (опционально)
            await Task.Delay(200, ct);

            var response = await _httpClient.GetAsync(pdocUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Ищем ссылку на скачивание файла
            // Обычно это download.aspx?fileid=...
            // Иногда ссылка может быть просто текстом файла, надо искать <a> рядом
            var downloadLink = doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'download.aspx') and contains(@href, 'fileid=')]");

            if (downloadLink == null)
            {
                return false;
            }

            var downloadUrl = downloadLink.GetAttributeValue("href", "");
            var fileName = downloadLink.InnerText.Trim(); // Обычно "план бассеин баня.pdf"

            if (string.IsNullOrEmpty(downloadUrl))
            {
                return false;
            }

            // Абсолютный URL
            if (!downloadUrl.StartsWith("http"))
            {
                downloadUrl = "https://bankrot.cdtrf.ru/" + downloadUrl;
            }

            // Защита от дубликатов ссылок
            if (processedUrls.Contains(downloadUrl))
            {
                return false;
            }
            processedUrls.Add(downloadUrl);

            // Определяем тип файла по расширению имени (не по URL, т.к. там .aspx)
            var extension = Path.GetExtension(fileName).ToLower();
            bool isImage = IsImageExtension(extension);

            // Если имя файла пустое или странное, пробуем угадать или ставим дефолт
            if (string.IsNullOrEmpty(extension))
            {
                // По умолчанию считаем документом
                isImage = false;
                extension = ".bin";
            }

            // Скачиваем байты
            var fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl, ct);
            if (fileBytes.Length == 0)
            {
                return false;
            }

            // Генерируем путь для S3
            // lots/{lotId}/{guid}.{ext}
            var s3FileName = $"lots/{lot.Id}/{Guid.NewGuid()}{extension}";
            var s3Url = await _fileStorage.UploadAsync(fileBytes, s3FileName);

            if (isImage)
            {
                // Добавляем в коллекцию картинок
                lot.Images.Add(new LotImage
                {
                    LotId = lot.Id,
                    Url = s3Url,
                    Order = currentImgOrder
                });

                _logger.LogInformation("Lot {LotNumber}: Загружено фото {FileName}", lot.LotNumber, fileName);

                return true; // Сообщаем, что добавили картинку
            }
            else
            {
                if (lot.Documents == null)
                {
                    lot.Documents = new List<LotDocument>(); // На всякий случай инициализируем, если EF не сделал
                }

                lot.Documents.Add(new LotDocument
                {
                    LotId = lot.Id,
                    Url = s3Url,
                    Title = !string.IsNullOrEmpty(fileName) ? fileName : title,
                    Extension = extension
                });

                _logger.LogInformation("Lot {LotNumber}: Загружен документ {FileName}", lot.LotNumber, fileName);

                return false; // Документ не влияет на порядок картинок
            }
        }

        private bool IsImageExtension(string ext)
        {
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".webp";
        }

        private void ProcessPriceSchedule(Lot lot, HtmlDocument doc)
        {
            // Ищем любой элемент (td, th, b, span...), содержащий ключевую фразу.
            // XPath ".//*" ищет на любой глубине, text() берет именно текстовый узел.
            var headerNode = doc.DocumentNode.SelectSingleNode("//*[contains(text(), 'Начало периода действия цены')]");

            if (headerNode == null)
            {
                _logger.LogWarning("Lot {LotNumber}: Не найдена таблица с графиком (header row missing).", lot.LotNumber);
                return;
            }

            // Поднимаемся вверх до <table>.
            // Безопасный поиск предка, так как headerNode может быть глубоко (td -> b -> text).
            var table = headerNode.Ancestors("table").FirstOrDefault();
            if (table == null)
            {
                // Иногда таблица сверстана дивами, но на ЦДТ обычно table.
                // Если table нет, возможно, мы нашли текст в описании, а не в таблице.
                _logger.LogWarning("Lot {LotNumber}: Текст найден, но он не внутри тега <table>.", lot.LotNumber);
                return;
            }

            var rows = table.SelectNodes(".//tr");
            if (rows == null) return;

            lot.PriceSchedules.Clear();

            // Пропускаем строки до заголовка включительно и берем данные
            // Но надежнее просто фильтровать строки, которые содержат даты
            foreach (var row in rows)
            {
                var cols = row.SelectNodes("td");
                if (cols == null || cols.Count < 4) continue;

                // Ожидаем структуру: 
                // [0] №
                // [1] Начало (19.01.2026 00:00:00)
                // [2] Конец (27.01.2026 23:59:59)
                // [3] Цена (1 260 000,00)

                var startText = cols[1].InnerText.Trim();
                var endText = cols[2].InnerText.Trim();
                var priceText = cols[3].InnerText.Trim();

                // Проверка, что это строка данных, а не заголовок
                if (startText.Contains("Начало")) continue;

                if (TryParseDateTime(startText, out var startUtc) &&
                    TryParseDateTime(endText, out var endUtc) &&
                    TryParsePrice(priceText, out var price))
                {
                    lot.PriceSchedules.Add(new LotPriceSchedule
                    {
                        LotId = lot.Id,
                        StartDate = startUtc,
                        EndDate = endUtc,
                        Price = price,
                        // Задаток на ЦДТ обычно не в этой таблице, ставим 0 или парсим из текста выше
                        Deposit = 0
                    });
                }
            }

            _logger.LogInformation("Lot {LotNumber}: Найдено {Count} этапов снижения цены.", lot.LotNumber, lot.PriceSchedules.Count);
        }

        private async Task ProcessImagesStubAsync(Lot lot, CancellationToken ct)
        {
            // Заглушка
            await Task.CompletedTask;
            // _logger.LogDebug("Парсинг фото для ЦДТ пока не реализован.");
        }

        // --- Вспомогательные методы ---

        private string CleanTradeNumber(string tradeNumber)
        {
            // Если в базе лежит "321404", возвращаем как есть.
            if (string.IsNullOrEmpty(tradeNumber)) return "";
            return tradeNumber.Trim();
        }

        private bool IsPublicOffer(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return type.Contains("Публичное", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryParsePrice(string raw, out decimal price)
        {
            // Убираем пробелы (в т.ч. неразрывные) и "руб"
            var clean = raw
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace("руб", "")
                .Replace(".", ",") // На случай если разделитель точка, а культура RU
                .Trim();

            // Используем русскую культуру для парсинга "1,00"
            return decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("ru-RU"), out price);
        }

        private bool TryParseDateTime(string raw, out DateTime dateUtc)
        {
            dateUtc = DateTime.MinValue;
            // Формат в HTML: 19.01.2026 00:00:00
            var clean = raw.Replace("\u00A0", " ").Trim();

            if (DateTime.TryParse(clean, new CultureInfo("ru-RU"), DateTimeStyles.None, out var dt))
            {
                dateUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc); // Предполагаем, что на сайте время МСК/локальное, но сохраняем как UTC для базы
                return true;
            }
            return false;
        }

        private void HandleError(Bidding bidding, Exception ex)
        {
            if (bidding.EnrichmentState == null)
            {
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
                bidding.EnrichmentState.RetryCount++;
                bidding.EnrichmentState.LastAttemptAt = DateTime.UtcNow;
                bidding.EnrichmentState.LastError = ex.Message;
            }
        }
    }
}
