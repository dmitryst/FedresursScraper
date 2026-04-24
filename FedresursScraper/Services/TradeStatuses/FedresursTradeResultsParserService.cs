using System.Globalization;
using System.Text.RegularExpressions;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace FedresursScraper.Services;

public class FedresursTradeResultsParserService
{
    private readonly ILogger<FedresursTradeResultsParserService> _logger;
    private readonly LotsDbContext _dbContext;
    private readonly IWebDriverFactory _webDriverFactory;
    private readonly IConfiguration _configuration;
    private readonly Random _random = new();

    private const string BaseUrl = "https://fedresurs.ru/biddings";

    private static readonly string[] TargetMessageTypes =
    {
        "Торги не состоялись",
        "Результаты торгов",
        "Отмена торгов"
    };

    public FedresursTradeResultsParserService(
        ILogger<FedresursTradeResultsParserService> logger,
        LotsDbContext dbContext,
        IWebDriverFactory webDriverFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _dbContext = dbContext;
        _webDriverFactory = webDriverFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Парсинг пачкой для фонового сервиса
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        var batchSize = _configuration.GetValue<int>("Parsing:ResultsBatchSize", 50);
        var now = DateTime.UtcNow;

        var biddingsToProcess = await _dbContext.Biddings
            .Include(b => b.Lots)
            .Where(b => !b.IsTradeStatusesFinalized &&
                        (b.NextStatusCheckAt == null || b.NextStatusCheckAt <= now) &&
                        b.Lots.Any(l => l.LotNumber != null && l.LotNumber != ""))
            .OrderBy(b => b.NextStatusCheckAt ?? DateTime.MinValue) // Берем самые старые
            .Take(batchSize)
            .ToListAsync(stoppingToken);

        if (!biddingsToProcess.Any())
        {
            _logger.LogInformation("Очередь пуста. Нет торгов для проверки результатов.");
            return;
        }

        using var driver = _webDriverFactory.CreateDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

        foreach (var bidding in biddingsToProcess)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessSingleBiddingInternalAsync(driver, wait, bidding, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке торгов {BiddingId}", bidding.Id);
            }

            await Task.Delay(_random.Next(5000, 10000), stoppingToken);
        }
    }

    /// <summary>
    /// Парсинг конкретных торгов для API
    /// </summary>
    public async Task<bool> ProcessSingleBiddingAsync(Guid biddingId, CancellationToken cancellationToken)
    {
        var bidding = await _dbContext.Biddings
            .Include(b => b.Lots)
            .FirstOrDefaultAsync(b => b.Id == biddingId, cancellationToken);

        if (bidding == null) return false;

        using var driver = _webDriverFactory.CreateDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

        bool allFinalized = await ProcessSingleBiddingInternalAsync(driver, wait, bidding, cancellationToken);

        if (allFinalized)
        {
            bidding.IsTradeStatusesFinalized = true;
            bidding.NextStatusCheckAt = null;
            _logger.LogInformation("Все лоты торгов {BiddingId} получили результаты. Торги финализированы.", bidding.Id);
        }
        else
        {
            bidding.ScheduleNextCheck(DateTime.UtcNow);

            // Записываем обновление в таблицу для экспорта на прод
            var scheduleUpdate = new BiddingScheduleUpdate
            {
                Id = Guid.NewGuid(),
                BiddingId = bidding.Id,
                NextStatusCheckAt = bidding.NextStatusCheckAt.Value,
                IsExported = false
            };

            _dbContext.BiddingScheduleUpdates.Add(scheduleUpdate);

            _logger.LogInformation("Торги {Id} не завершены. Перепланировано на {Date}",
                biddingId, bidding.NextStatusCheckAt);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<bool> ProcessSingleBiddingInternalAsync(IWebDriver driver, WebDriverWait wait, Bidding bidding, CancellationToken stoppingToken)
    {
        var messagesUrl = $"{BaseUrl}/{bidding.Id}/messages";
        _logger.LogInformation("Проверка сообщений для торгов {BiddingId}: {Url}", bidding.Id, messagesUrl);

        driver.Navigate().GoToUrl(messagesUrl);
        await Task.Delay(_random.Next(3000, 5000), stoppingToken);

        // Получаем ID сообщений, которые уже парсили для этих торгов
        var parsedMessageIds = await _dbContext.LotTradeResults
            .Where(r => r.BiddingId == bidding.Id)
            .Select(r => r.MessageId)
            .ToListAsync(stoppingToken);

        var messagesToParse = new List<(Guid MessageId, string Url, string Type, DateTime Date)>();
        bool stopPagination = false;
        int lastCount = 0;

        // Пагинация списка сообщений
        while (!stopPagination && !stoppingToken.IsCancellationRequested)
        {
            var messageCards = driver.FindElements(By.CssSelector("bidding-message-card"));

            for (int i = lastCount; i < messageCards.Count; i++)
            {
                var card = messageCards[i];
                var type = card.FindElement(By.CssSelector(".message-type-name")).Text.Trim();

                if (!TargetMessageTypes.Contains(type)) continue;

                var linkElement = card.FindElement(By.CssSelector(".message-info-link a.underlined"));
                var href = linkElement.GetAttribute("href");
                var msgIdStr = href?.Split('/').LastOrDefault();

                if (!Guid.TryParse(msgIdStr, out var messageId)) continue;

                // Если дошли до сообщения, которое уже есть в БД — останавливаем прокрутку вглубь истории
                if (parsedMessageIds.Contains(messageId))
                {
                    stopPagination = true;
                    break;
                }

                var dateStr = card.FindElement(By.CssSelector(".action-date-description-value")).Text.Trim();
                DateTime.TryParseExact(dateStr, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var eventDate);

                messagesToParse.Add((messageId, href!, type, eventDate));
            }

            if (stopPagination) break;

            lastCount = messageCards.Count;

            var loadMoreBtn = driver.FindElements(By.CssSelector(".more_btn_wrapper .more_btn")).FirstOrDefault();
            if (loadMoreBtn != null && loadMoreBtn.Displayed)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", loadMoreBtn);
                await Task.Delay(_random.Next(2000, 5000), stoppingToken);
                loadMoreBtn.Click();

                try { wait.Until(d => d.FindElements(By.CssSelector("bidding-message-card")).Count > lastCount); }
                catch (WebDriverTimeoutException) { break; }
            }
            else
            {
                break; // Кнопки больше нет
            }
        }

        // Парсинг самих сообщений
        bool hasNewResults = false;
        foreach (var msg in messagesToParse)
        {
            driver.Navigate().GoToUrl(msg.Url);
            await Task.Delay(_random.Next(3000, 5000), stoppingToken);

            if (msg.Type == "Торги не состоялись")
            {
                hasNewResults |= ParseFailedBiddingMessage(driver, _dbContext, bidding, msg.MessageId, msg.Date);
            }
            else if (msg.Type == "Результаты торгов")
            {
                hasNewResults |= ParseSuccessBiddingMessage(driver, _dbContext, bidding, msg.MessageId, msg.Date);
            }
            else if (msg.Type == "Отмена торгов")
            {
                hasNewResults |= ParseCancelledBiddingMessage(driver, _dbContext, bidding, msg.MessageId, msg.Date);
            }
        }

        if (hasNewResults)
        {
            await _dbContext.SaveChangesAsync(stoppingToken);

            // Проверяем, закрылись ли все активные лоты
            var activeLots = bidding.Lots.Where(l => l.IsActive()).ToList();
            bool allFinalized = true;

            foreach (var lot in activeLots)
            {
                var normalizedNumber = NormalizeLotNumber(lot.LotNumber);
                var resultExists = _dbContext.LotTradeResults.Any(r => r.BiddingId == bidding.Id && r.LotNumber == normalizedNumber);

                if (!resultExists)
                {
                    allFinalized = false;
                    break;
                }
            }

            return allFinalized;
        }

        return false;
    }

    private bool ParseFailedBiddingMessage(IWebDriver driver, LotsDbContext dbContext, Bidding bidding, Guid messageId, DateTime eventDate)
    {
        // Ищем все div-контейнеры внутри компонента, у которых есть заголовок лота
        var lotBlocks = driver.FindElements(By.XPath("//bidding-message-lot-reason/div[./div[contains(@class, 'info-header')]]"));
        bool processedAny = false;

        foreach (var block in lotBlocks)
        {
            var headerText = block.FindElement(By.CssSelector(".info-header")).Text;
            var lotNumber = ExtractLotNumber(headerText);
            if (lotNumber == null) continue;

            var items = block.FindElements(By.CssSelector(".info-item"));
            var reason = items.FirstOrDefault(i => i.FindElement(By.CssSelector(".info-item-name")).Text.Contains("Причина"))?
                              .FindElement(By.CssSelector(".info-item-value")).Text.Trim();

            var result = new LotTradeResult
            {
                Id = Guid.NewGuid(),
                BiddingId = bidding.Id,
                MessageId = messageId,
                LotNumber = lotNumber,
                EventType = "Торги не состоялись",
                EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
                IsExportedToProd = false
            };

            dbContext.LotTradeResults.Add(result);
            processedAny = true;
        }

        return processedAny;
    }

    private bool ParseSuccessBiddingMessage(IWebDriver driver, LotsDbContext dbContext, Bidding bidding, Guid messageId, DateTime eventDate)
    {
        var lotBlocks = driver.FindElements(By.XPath("//bidding-message-biddingresult/div[./div[contains(@class, 'info-header')]]"));
        bool processedAny = false;

        foreach (var block in lotBlocks)
        {
            var headerText = block.FindElement(By.CssSelector(".info-header")).Text;
            var lotNumber = ExtractLotNumber(headerText);
            if (lotNumber == null) continue;

            // Вспомогательная функция для поиска значения по названию поля в блоке
            string? GetValue(string label) => block.FindElements(By.CssSelector(".info-item"))
                .FirstOrDefault(i => i.FindElement(By.CssSelector(".info-item-name")).Text.Contains(label))?
                .FindElement(By.CssSelector(".info-item-value")).Text.Trim();

            var lotStatus = GetValue("Статус");
            var justification = GetValue("Обоснование принятого решения");
            var finalPrice = ParsePrice(GetValue("Предложенная цена") ?? GetValue("Цена приобретения"));

            // Пробуем найти Победителя, если нет - Участника
            var winnerRaw = GetValue("Победитель") ?? GetValue("Участник");

            string? winnerName = null;
            string? winnerInn = null;

            if (!string.IsNullOrEmpty(winnerRaw))
            {
                // Ищем "ИНН", игнорируем любые не-цифры (переносы, двоеточия, пробелы) и берем 10-12 цифр
                var innMatch = Regex.Match(winnerRaw, @"ИНН[^\d]*(\d{10,12})", RegexOptions.IgnoreCase);
                if (innMatch.Success)
                {
                    winnerInn = innMatch.Groups[1].Value;

                    // Имя — это всё, что до слова "ИНН"
                    var rawName = winnerRaw.Substring(0, innMatch.Index);

                    // Заменяем любые переносы строк и множественные пробелы на один пробел, 
                    // и убираем лишнюю пунктуацию по краям
                    winnerName = Regex.Replace(rawName, @"\s+", " ").Trim(' ', '(', ',', '-');
                }
                else
                {
                    // Если ИНН не найден, просто чистим строку от переносов
                    winnerName = Regex.Replace(winnerRaw, @"\s+", " ").Trim();
                }
            }

            var result = new LotTradeResult
            {
                Id = Guid.NewGuid(),
                BiddingId = bidding.Id,
                MessageId = messageId,
                LotNumber = lotNumber,
                EventType = "Результаты торгов",
                EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
                Status = lotStatus,
                DecisionJustification = justification,
                FinalPrice = finalPrice,
                WinnerName = winnerName,
                WinnerInn = winnerInn,
                CreatedAt = DateTime.UtcNow,
                IsExportedToProd = false
            };

            dbContext.LotTradeResults.Add(result);
            processedAny = true;
        }

        return processedAny;
    }

    private bool ParseCancelledBiddingMessage(IWebDriver driver, LotsDbContext dbContext, Bidding bidding, Guid messageId, DateTime eventDate)
    {
        // Ищем блоки лотов (как в "Торги не состоялись")
        var lotBlocks = driver.FindElements(By.XPath("//bidding-message-lot-reason/div[./div[contains(@class, 'info-header')]]"));
        bool processedAny = false;

        if (lotBlocks.Any())
        {
            // Сценарий 1: Отменены конкретные лоты
            foreach (var block in lotBlocks)
            {
                var headerText = block.FindElement(By.CssSelector(".info-header")).Text;
                var lotNumber = ExtractLotNumber(headerText);
                if (lotNumber == null) continue;

                var items = block.FindElements(By.CssSelector(".info-item"));
                var reason = items.FirstOrDefault(i => i.FindElement(By.CssSelector(".info-item-name")).Text.Contains("Причина"))?
                                  .FindElement(By.CssSelector(".info-item-value")).Text.Trim();

                AddLotTradeResult(dbContext, bidding.Id, messageId, lotNumber, "Отмена торгов", eventDate, reason);
                processedAny = true;
            }
        }
        else
        {
            // Сценарий 2: Отменены торги целиком без перечисления лотов
            // Пытаемся найти общую причину отмены в тексте сообщения
            string? reason = null;
            try
            {
                var reasonNode = driver.FindElements(By.XPath("//div[contains(@class, 'info-item') and contains(., 'Причина')]//div[contains(@class, 'info-item-value')]")).FirstOrDefault();
                reason = reasonNode?.Text.Trim();
            }
            catch { /* Оставляем reason = null, если структура другая */ }

            // Применяем отмену ко всем активным лотам этих торгов, которые есть у нас в БД
            var activeLots = bidding.Lots.Where(l => l.IsActive() && !string.IsNullOrWhiteSpace(l.LotNumber)).ToList();

            foreach (var lot in activeLots)
            {
                var normalizedNumber = NormalizeLotNumber(lot.LotNumber);
                AddLotTradeResult(dbContext, bidding.Id, messageId, normalizedNumber, "Отмена торгов", eventDate, reason);
                processedAny = true;
            }
        }

        return processedAny;
    }

    // Вспомогательный метод для сокращения дублирования кода
    private void AddLotTradeResult(LotsDbContext dbContext, Guid biddingId, Guid messageId, string lotNumber, string eventType, DateTime eventDate, string? reason)
    {
        var result = new LotTradeResult
        {
            Id = Guid.NewGuid(),
            BiddingId = biddingId,
            MessageId = messageId,
            LotNumber = lotNumber,
            EventType = eventType,
            EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
            Reason = reason,
            CreatedAt = DateTime.UtcNow,
            IsExportedToProd = false
        };

        dbContext.LotTradeResults.Add(result);
    }

    private string? ExtractLotNumber(string headerText)
    {
        var match = Regex.Match(headerText, @"Лот №\s*(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string NormalizeLotNumber(string? lotNumber)
    {
        if (string.IsNullOrWhiteSpace(lotNumber)) return string.Empty;
        return Regex.Replace(lotNumber.Trim(), @"(?i)\s*лот\s*№?\s*", "").Trim();
    }

    private decimal? ParsePrice(string? priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return null;

        // Убираем пробелы, знак рубля и лишние символы: "26 669,00 ₽" -> "26669,00"
        var cleanPrice = Regex.Replace(priceText, @"[^\d,.]", "").Replace(".", ",");

        if (decimal.TryParse(cleanPrice, NumberStyles.Any, new CultureInfo("ru-RU"), out var price))
        {
            return price;
        }

        return null;
    }
}