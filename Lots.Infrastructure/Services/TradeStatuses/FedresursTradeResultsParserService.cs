using System.Globalization;
using System.Text.RegularExpressions;
using Lots.Data;
using Lots.Data.Entities;
using static Lots.Data.Entities.Lot;
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

    private const string CollateralCreditorCompletionMessageType =
        "О завершении торгов вследствие оставления конкурсным кредитором предмета залога за собой";

    private const string CollateralCreditorCompletionReason =
        "Вследствие оставления конкурсным кредитором предмета залога за собой";

    private const string SuspendedBiddingMessageType = SuspendedTradeStatus;

    private static readonly string[] TargetMessageTypes =
    {
        "Торги не состоялись",
        "Результаты торгов",
        "Отмена торгов",
        "Торги приостановлены",
        CollateralCreditorCompletionMessageType
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
        var batchSize = _configuration.GetValue<int>("BackgroundServices:FedresursTradeResults:ResultsBatchSize", 100);
        var now = DateTime.UtcNow;

        var biddingsToProcess = await _dbContext.Biddings
            .Include(b => b.Lots)
            .Where(b => !b.IsTradeStatusesFinalized &&
                        (b.NextStatusCheckAt == null || b.NextStatusCheckAt <= now) &&
                        b.Lots.Any(l => l.LotNumber != null && l.LotNumber != ""))
            //.Where(b => b.Lots.Count < 30)  // пока не берем торги, у которых 30 и более лотов, будем такие торги обрабатывать через scrape контроллер
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
                // Увеличиваем счетчик попыток проверки статуса
                bidding.StatusCheckAttempts++;

                // Получаем результат: все ли лоты завершены
                bool allFinalized = await ProcessSingleBiddingInternalAsync(driver, wait, bidding, stoppingToken);

                // Применяем логику финализации или перепланирования
                if (allFinalized)
                {
                    bidding.IsTradeStatusesFinalized = true;
                    bidding.NextStatusCheckAt = null;
                    _logger.LogInformation("Все лоты торгов {BiddingId} получили результаты. Торги финализированы.", bidding.Id);
                }
                else
                {
                    await ScheduleBiddingNextCheckAsync(bidding, stoppingToken);
                }

                // Сохраняем изменения даты и статусов в БД
                await _dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке торгов {BiddingId}", bidding.Id);

                // Очищаем контекст от "отравленных" сущностей, 
                // чтобы эта ошибка не сломала сохранение следующих торгов в цикле.
                _dbContext.ChangeTracker.Clear();

                try
                {
                    // Выполняем прямой SQL-запрос (через ExecuteUpdate), чтобы сдвинуть NextStatusCheckAt.
                    // ExecuteUpdate не использует ChangeTracker, поэтому сработает безопасно и быстро.
                    // Сдвигаем на 2 часа вперед.
                    await _dbContext.Biddings
                        .Where(b => b.Id == bidding.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.NextStatusCheckAt, DateTime.UtcNow.AddHours(2)), stoppingToken);
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Критическая ошибка: не удалось сдвинуть NextStatusCheckAt для сбойных торгов {BiddingId}", bidding.Id);
                }
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

        // Увеличиваем счетчик попыток проверки статуса
        bidding.StatusCheckAttempts++;

        bool allFinalized = await ProcessSingleBiddingInternalAsync(driver, wait, bidding, cancellationToken);

        if (allFinalized)
        {
            bidding.IsTradeStatusesFinalized = true;
            bidding.NextStatusCheckAt = null;
            _logger.LogInformation("Все лоты торгов {BiddingId} получили результаты. Торги финализированы.", bidding.Id);
        }
        else
        {
            await ScheduleBiddingNextCheckAsync(bidding, cancellationToken);
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

                if (!IsTargetMessageType(type)) continue;

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
            else if (msg.Type == SuspendedBiddingMessageType)
            {
                hasNewResults |= ParseSuspendedBiddingMessage(driver, _dbContext, bidding, msg.MessageId, msg.Date);
            }
            else if (IsCollateralCreditorCompletionMessage(msg.Type))
            {
                hasNewResults |= ParseCollateralCreditorKeepsBiddingMessage(driver, _dbContext, bidding, msg.MessageId, msg.Date);
            }
        }

        if (hasNewResults)
        {
            await _dbContext.SaveChangesAsync(stoppingToken);

            // Проверяем, закрылись ли все активные лоты
            var tradeResults = await _dbContext.LotTradeResults
                .Where(r => r.BiddingId == bidding.Id)
                .ToListAsync(stoppingToken);

            return TradeResultsScheduleHelper.AllActiveLotsHaveFinalizingResults(bidding, tradeResults);
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
            var finalPrice = ParsePrice(GetValue("Цена лота") ?? GetValue("Предложенная цена") ?? GetValue("Цена приобретения"));

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

    private bool ParseCollateralCreditorKeepsBiddingMessage(IWebDriver driver, LotsDbContext dbContext, Bidding bidding, Guid messageId, DateTime eventDate)
    {
        var component = driver.FindElements(By.CssSelector("bidding-message-biddingendbankruptcycreditor")).FirstOrDefault();
        if (component == null) return false;

        bool processedAny = false;
        var processedLotNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in component.FindElements(By.CssSelector(".info-item")))
        {
            var name = item.FindElement(By.CssSelector(".info-item-name")).Text.Trim();
            if (!name.Contains("Лоты", StringComparison.OrdinalIgnoreCase)) continue;

            var lotsValue = item.FindElement(By.CssSelector(".info-item-value")).Text.Trim();
            foreach (var lotNumber in ParseLotNumbersFromList(lotsValue))
            {
                if (!processedLotNumbers.Add(lotNumber)) continue;

                var result = new LotTradeResult
                {
                    Id = Guid.NewGuid(),
                    BiddingId = bidding.Id,
                    MessageId = messageId,
                    LotNumber = lotNumber,
                    EventType = CollateralCreditorCompletionMessageType,
                    EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
                    Reason = CollateralCreditorCompletionReason,
                    Status = "Завершенные",
                    CreatedAt = DateTime.UtcNow,
                    IsExportedToProd = false
                };

                dbContext.LotTradeResults.Add(result);
                processedAny = true;
            }
        }

        return processedAny;
    }

    private bool ParseSuspendedBiddingMessage(IWebDriver driver, LotsDbContext dbContext, Bidding bidding, Guid messageId, DateTime eventDate)
    {
        var lotBlocks = driver.FindElements(By.XPath("//bidding-message-lot-reason/div[./div[contains(@class, 'info-header')]]"));
        bool processedAny = false;

        if (lotBlocks.Any())
        {
            foreach (var block in lotBlocks)
            {
                var headerText = block.FindElement(By.CssSelector(".info-header")).Text;
                var lotNumber = ExtractLotNumber(headerText);
                if (lotNumber == null) continue;

                var items = block.FindElements(By.CssSelector(".info-item"));
                var reason = items.FirstOrDefault(i => i.FindElement(By.CssSelector(".info-item-name")).Text.Contains("Причина"))?
                                  .FindElement(By.CssSelector(".info-item-value")).Text.Trim();

                AddSuspendedLotTradeResult(dbContext, bidding.Id, messageId, lotNumber, eventDate, reason);
                processedAny = true;
            }
        }
        else
        {
            string? reason = null;
            try
            {
                var reasonNode = driver.FindElements(By.XPath("//div[contains(@class, 'info-item') and contains(., 'Причина')]//div[contains(@class, 'info-item-value')]")).FirstOrDefault();
                reason = reasonNode?.Text.Trim();
            }
            catch { /* Оставляем reason = null, если структура другая */ }

            var activeLots = bidding.Lots.Where(l => l.IsActive() && !string.IsNullOrWhiteSpace(l.LotNumber)).ToList();

            foreach (var lot in activeLots)
            {
                var normalizedNumber = NormalizeLotNumber(lot.LotNumber);
                AddSuspendedLotTradeResult(dbContext, bidding.Id, messageId, normalizedNumber, eventDate, reason);
                processedAny = true;
            }
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

    private void AddSuspendedLotTradeResult(LotsDbContext dbContext, Guid biddingId, Guid messageId, string lotNumber, DateTime eventDate, string? reason)
    {
        var result = new LotTradeResult
        {
            Id = Guid.NewGuid(),
            BiddingId = biddingId,
            MessageId = messageId,
            LotNumber = lotNumber,
            EventType = SuspendedBiddingMessageType,
            EventDate = DateTime.SpecifyKind(eventDate, DateTimeKind.Utc),
            Reason = reason,
            Status = SuspendedBiddingMessageType,
            CreatedAt = DateTime.UtcNow,
            IsExportedToProd = false
        };

        dbContext.LotTradeResults.Add(result);
    }

    private static bool IsTargetMessageType(string type) =>
        TargetMessageTypes.Contains(type) || IsCollateralCreditorCompletionMessage(type);

    private static bool IsCollateralCreditorCompletionMessage(string type) =>
        type.Contains("оставления конкурсным кредитором предмета залога за собой", StringComparison.OrdinalIgnoreCase);

    private async Task ScheduleBiddingNextCheckAsync(Bidding bidding, CancellationToken cancellationToken)
    {
        var suspendedRecheckDays = _configuration.GetValue<int>(
            "BackgroundServices:FedresursTradeResults:SuspendedRecheckDays", 14);

        var tradeResults = await _dbContext.LotTradeResults
            .Where(r => r.BiddingId == bidding.Id)
            .ToListAsync(cancellationToken);

        var useSuspendedInterval = TradeResultsScheduleHelper.ShouldUseSuspendedRecheckInterval(bidding, tradeResults);
        bidding.ScheduleNextCheck(DateTime.UtcNow, suspendedRecheckDays, useSuspendedInterval);

        if (bidding.NextStatusCheckAt.HasValue)
        {
            var scheduleUpdate = new BiddingScheduleUpdate
            {
                Id = Guid.NewGuid(),
                BiddingId = bidding.Id,
                NextStatusCheckAt = bidding.NextStatusCheckAt.Value,
                IsExported = false
            };

            _dbContext.BiddingScheduleUpdates.Add(scheduleUpdate);
        }

        _logger.LogInformation(
            "Торги {Id} не завершены. Перепланировано на {Date}{SuspendedNote}",
            bidding.Id,
            bidding.NextStatusCheckAt,
            useSuspendedInterval ? $" (интервал для приостановленных: {suspendedRecheckDays} дн.)" : string.Empty);
    }

    private IEnumerable<string> ParseLotNumbersFromList(string lotsValue)
    {
        if (string.IsNullOrWhiteSpace(lotsValue)) yield break;

        foreach (var part in lotsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeLotNumber(part);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !normalized.Contains("нет данных", StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized;
            }
        }
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