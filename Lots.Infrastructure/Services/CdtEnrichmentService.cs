using HtmlAgilityPack;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

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
        private readonly ILotsFileStorageService _fileStorage;
        private readonly ILogger<CdtEnrichmentService> _logger;

        // Настройки батчей
        private const int BatchSize = 5;
        private const int MaxRetryCount = 3;

        public CdtEnrichmentService(
            LotsDbContext context,
            HttpClient httpClient,
            ILotsFileStorageService fileStorage,
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
                .Include(b => b.Lots)
                    .ThenInclude(l => l.PriceSchedules)
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
                .Include(b => b.Lots)
                    .ThenInclude(l => l.PriceSchedules)
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
            var tradeId = CleanTradeNumber(bidding.TradeNumber);
            var url = $"https://torgi.cdtrf.ru/trades/{tradeId}";

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Не удалось получить страницу {url}. Status: {response.StatusCode}");
            }

            var htmlContent = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            // Извлекаем JSON из тега <pre style="display:none;">
            var preNode = doc.DocumentNode.SelectSingleNode("//pre[@style='display:none;']");
            CdtrfTradeData tradeData = null;

            if (preNode != null)
            {
                var jsonStr = System.Net.WebUtility.HtmlDecode(preNode.InnerText);
                try
                {
                    tradeData = JsonSerializer.Deserialize<CdtrfTradeData>(jsonStr, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Не удалось распарсить JSON из pre тега для торгов {TradeNumber}", tradeId);
                }
            }

            foreach (var lot in bidding.Lots)
            {
                // Очищаем старые данные перед парсингом
                lot.Images.Clear();
                lot.Documents.Clear();
                lot.PriceSchedules.Clear();

                if (tradeData?.Lot != null)
                {
                    await ProcessImagesAsync(lot, tradeData.Lot, ct);

                    if (IsPublicOffer(bidding.Type))
                    {
                        ProcessPriceSchedule(lot, tradeData.Lot);
                    }
                }
                else
                {
                    _logger.LogWarning("Данные лота не найдены в JSON или JSON отсутствует для торгов {TradeNumber}. Парсинг завершен без изображений.", tradeId);
                }
            }
        }

        private async Task ProcessImagesAsync(Lot lot, CdtrfLot cdtrfLot, CancellationToken ct)
        {
            if (cdtrfLot.Images == null || !cdtrfLot.Images.Any())
            {
                _logger.LogInformation("Lot {LotNumber}: Картинки не найдены.", lot.LotNumber);
                return;
            }

            var images = cdtrfLot.Images.OrderBy(i => i.Position).ToList();

            foreach (var img in images)
            {
                if (ct.IsCancellationRequested) break;

                // URL для скачивания картинки (большой размер)
                // Пример: https://webapi.torgi.cdtrf.ru/LotImage/public?LotImageSize=Large&ImageId=...&LotId=...&TradeId=...
                var imageUrl = $"https://webapi.torgi.cdtrf.ru/LotImage/public?LotImageSize=Large&ImageId={img.Id}&LotId={cdtrfLot.TradeLotId}&TradeId={cdtrfLot.TradeId}";

                try
                {
                    await Task.Delay(200, ct); // Небольшая пауза

                    var response = await _httpClient.GetAsync(imageUrl, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Не удалось скачать картинку {Url}. Статус: {StatusCode}", imageUrl, response.StatusCode);
                        continue;
                    }

                    var fileBytes = await response.Content.ReadAsByteArrayAsync(ct);
                    if (fileBytes.Length == 0) continue;

                    var extension = ".jpg"; // По умолчанию, так как обычно WebAPI отдает JPEG, либо можно определять по Magic Bytes
                    var s3FileName = $"lots/{lot.Id}/{Guid.NewGuid()}{extension}";
                    var s3Url = await _fileStorage.UploadAsync(fileBytes, s3FileName);

                    lot.Images.Add(new LotImage
                    {
                        LotId = lot.Id,
                        Url = s3Url,
                        Order = img.Position
                    });

                    _logger.LogInformation("Lot {LotNumber}: Загружено фото {ImageId}", lot.LotNumber, img.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Lot {LotNumber}: Ошибка при загрузке картинки {ImageId}: {Error}", lot.LotNumber, img.Id, ex.Message);
                }
            }
        }

        private void ProcessPriceSchedule(Lot lot, CdtrfLot cdtrfLot)
        {
            if (cdtrfLot.LotScheduleItems == null || !cdtrfLot.LotScheduleItems.Any())
            {
                return;
            }

            foreach (var item in cdtrfLot.LotScheduleItems)
            {
                if (TryParseDateTime(item.StartTime, out var startUtc) &&
                    TryParseDateTime(item.EndTime, out var endUtc) &&
                    TryParsePrice(item.Price, out var price))
                {
                    lot.PriceSchedules.Add(new LotPriceSchedule
                    {
                        LotId = lot.Id,
                        StartDate = startUtc,
                        EndDate = endUtc,
                        Price = price,
                        Deposit = 0 // Задаток обычно указан в другом месте
                    });
                }
            }

            _logger.LogInformation("Lot {LotNumber}: Найдено {Count} этапов снижения цены.", lot.LotNumber, lot.PriceSchedules.Count);
        }

        // --- Вспомогательные методы ---

        private string CleanTradeNumber(string tradeNumber)
        {
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
            price = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            var clean = raw
                .Replace(" ", "")
                .Replace("\u00A0", "")
                .Replace("руб", "")
                .Replace(".", ",")
                .Trim();

            return decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("ru-RU"), out price);
        }

        private bool TryParseDateTime(string raw, out DateTime dateUtc)
        {
            dateUtc = DateTime.MinValue;
            if (string.IsNullOrEmpty(raw)) return false;

            var clean = raw.Replace("\u00A0", " ").Trim();

            if (DateTime.TryParse(clean, new CultureInfo("ru-RU"), DateTimeStyles.None, out var dt))
            {
                // Время на сайте указано по Москве (UTC+3)
                TimeZoneInfo moscowTimeZone;
                try
                {
                    moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
                }
                catch (TimeZoneNotFoundException)
                {
                    // На случай Windows
                    moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
                }

                dateUtc = TimeZoneInfo.ConvertTimeToUtc(dt, moscowTimeZone);
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

        // Модели для JSON из <pre style="display:none;">
        public class CdtrfTradeData
        {
            public CdtrfLot Lot { get; set; }
        }

        public class CdtrfLot
        {
            public int TradeId { get; set; }
            public int TradeLotId { get; set; }
            public List<CdtrfLotScheduleItem> LotScheduleItems { get; set; }
            public List<CdtrfImage> Images { get; set; }
        }

        public class CdtrfLotScheduleItem
        {
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public string Price { get; set; }
        }

        public class CdtrfImage
        {
            public string Id { get; set; }
            public int Position { get; set; }
            public bool IsMain { get; set; }
        }
    }
}
