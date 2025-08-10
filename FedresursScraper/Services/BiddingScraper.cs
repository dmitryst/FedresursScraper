using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using System.Globalization;
using System.Text.RegularExpressions;
using FedresursScraper.Services.Models;

namespace FedresursScraper.Services
{
    public class BiddingScraper : IBiddingScraper
    {
        private readonly ILogger<IBiddingScraper> _logger;

        public BiddingScraper(ILogger<IBiddingScraper> logger)
        {
            _logger = logger;
        }

        public async Task<BiddingInfo> ScrapeDataAsync(IWebDriver driver, Guid biddingId)
        {
            var url = $"https://fedresurs.ru/biddings/{biddingId}";

            driver.Navigate().GoToUrl(url);

            // ждем от 3 до 4.5 секунд пока прогрузится страница
            var random = new Random();
            await Task.Delay(3000 + random.Next(1500));

            // Выводим HTML страницы для анализа
            // var pageSource = driver.PageSource;
            // File.WriteAllText("debug.html", pageSource);

            string biddingType = ParseField(driver, "Вид торгов");
            string bidAcceptancePeriod = ParseField(driver, "Прием заявок");
            // string startPriceText = ParseField(driver, "Начальная цена");
            // string stepText = ParseField(driver, "Шаг аукциона");
            // string depositText = ParseField(driver, "Задаток");

            string announcementRawText = ParseField(driver, "Объявление о торгах");
            DateTime? announcementDate = ParseBiddingAnnouncementDate(announcementRawText);

            // var categories = ParseCategories(driver);
            var (description, viewingProcedure) = ParseAndSplitDescription(driver);

            return new BiddingInfo
            {
                Id = biddingId,
                AnnouncedAt = announcementDate,
                Type = biddingType,
                BidAcceptancePeriod = bidAcceptancePeriod,
                BankruptMessageId = ParseBankruptMessageId(driver),
                ViewingProcedure = viewingProcedure
            };
        }

        private string? ParseBidAcceptancePeriod(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || rawText.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Паттерн ищет две даты в формате "ДД.ММ.ГГГГ ЧЧ:ММ", разделенные словом "по"
            // Пример строки: "с 25.07.2024 12:00 по 30.08.2024 12:00"
            var match = Regex.Match(rawText, @"(\d{2}\.\d{2}\.\d{4}\s\d{2}:\d{2}).*?по.*?(\d{2}\.\d{2}\.\d{4}\s\d{2}:\d{2})", RegexOptions.IgnoreCase);

            if (match.Success && match.Groups.Count > 2)
            {
                // Форматируем результат в виде "Начало - Окончание"
                string startDate = match.Groups[1].Value;
                string endDate = match.Groups[2].Value;
                return $"{startDate} - {endDate}";
            }

            _logger.LogWarning("Не удалось распознать период приема заявок из строки: '{RawText}'", rawText);
            // Если формат не соответствует, возвращаем исходную строку как есть
            return rawText;
        }


        private Guid? ParseBankruptMessageId(IWebDriver driver)
        {
            try
            {
                // Находим ссылку внутри блока с объявлением о торгах
                var linkElement = driver.FindElement(By.XPath("//div[contains(text(),'Объявление о торгах')]/following-sibling::div//a"));
                var href = linkElement.GetAttribute("href");

                if (string.IsNullOrEmpty(href)) return null;

                // Извлекаем GUID из URL вида /bankruptmessages/guid
                var segments = href.Split('/');
                var guidString = segments.LastOrDefault();

                if (Guid.TryParse(guidString, out Guid messageId))
                {
                    return messageId;
                }
            }
            catch (NoSuchElementException)
            {
                _logger.LogWarning("Не найдена ссылка на сообщение о банкротстве на странице.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при парсинге BankruptMessageId.");
            }

            return null;
        }


        private DateTime? ParseBiddingAnnouncementDate(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || rawText.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Ищем паттерн "ДД.ММ.ГГГГ ЧЧ:ММ" с помощью регулярного выражения
            var match = Regex.Match(rawText, @"\d{2}\.\d{2}\.\d{4}\s\d{2}:\d{2}");

            if (match.Success)
            {
                // Пытаемся распарсить найденную дату, указывая точный формат
                if (DateTime.TryParseExact(match.Value, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime unspecifiedTime))
                {
                    try
                    {
                        TimeZoneInfo moscowTimeZone;
                        try
                        {
                            moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
                        }
                        catch (TimeZoneNotFoundException)
                        {
                            moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
                        }

                        // 2. Конвертируем "неопределенное" время в UTC, считая, что исходник был в MSK.
                        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedTime, moscowTimeZone);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation($"Не удалось найти часовой пояс для Москвы: {ex.Message}");
                        return null;
                    }
                }
            }

            return null;
        }

        // Универсальный метод для парсинга полей "ключ-значение"
        private string ParseField(IWebDriver driver, string fieldName)
        {
            try
            {
                // Используем FindElements, чтобы избежать исключения, если элемента нет
                var elements = driver.FindElements(By.XPath($"//div[contains(text(),'{fieldName}')]"));
                if (elements.Any())
                {
                    return elements.First().FindElement(By.XPath("./following-sibling::div")).Text.Trim();
                }
            }
            catch (Exception)
            {
                // Можно добавить логирование, если нужно
            }
            return "не найдено";
        }

        private List<string> ParseCategories(IWebDriver driver)
        {
            var categories = new List<string>();
            try
            {
                var elements = driver.FindElements(By.CssSelector(".lot-item-classifiers .lot-item-classifiers-element"));
                foreach (var el in elements)
                {
                    var text = el.Text.Trim();
                    if (!string.IsNullOrEmpty(text))
                        categories.Add(text);
                }
            }
            catch { }
            return categories;
        }

        private (string description, string viewingProcedure) ParseAndSplitDescription(IWebDriver driver)
        {
            // Описание объекта
            string description = "не найдено";
            try
            {
                // На странице может быть несколько лотов, возьмем первый lot-item-tradeobject
                var tradeObjectBlocks = driver.FindElements(By.CssSelector(".lot-item-tradeobject .content > div, .lot-item-tradeobject > div"));
                foreach (var block in tradeObjectBlocks)
                {
                    string text = block.Text.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        description = text;
                        break;
                    }
                }
                // Альтернатива на случай, если в одной ноде
                if (description == "не найдено")
                {
                    var lotObjectDiv = driver.FindElement(By.CssSelector(".lot-item-tradeobject"));
                    description = lotObjectDiv.Text.Trim();
                }
            }
            catch { }

            // Порядок ознакомления с имуществом должника – гибкая логика по двум маркерам
            var viewingProcedure = "";
            string rawDesc = description;
            try
            {
                // Маркер 1: "Порядок ознакомления с имуществом должника:"
                string marker1 = "Порядок ознакомления с имуществом должника:";
                int idx1 = rawDesc.IndexOf(marker1, StringComparison.OrdinalIgnoreCase);
                if (idx1 >= 0)
                {
                    description = rawDesc.Substring(0, idx1).Trim();
                    int afterStart = idx1 + marker1.Length;
                    if (afterStart <= rawDesc.Length)
                    {
                        viewingProcedure = rawDesc.Substring(afterStart).Trim();
                    }
                    viewingProcedure = viewingProcedure.TrimStart(new[] { ':', '.', ',', ' ' });
                }
                else
                {
                    // Маркер 2: "С имуществом можно ознакомиться"
                    string marker2 = "С имуществом можно ознакомиться";
                    int idx2 = rawDesc.IndexOf(marker2, StringComparison.OrdinalIgnoreCase);
                    if (idx2 >= 0)
                    {
                        description = rawDesc.Substring(0, idx2).Trim();
                        viewingProcedure = rawDesc.Substring(idx2).Trim();
                    }
                    else
                    {
                        // Если ни один маркер нет, описание целиком, viewingProcedure пустое
                        viewingProcedure = "";
                    }
                }
            }
            catch { }

            return (description, viewingProcedure);
        }


        private decimal? ParsePrice(string priceText)
        {
            if (string.IsNullOrWhiteSpace(priceText) || priceText.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
                return null;

            var cleanedString = Regex.Replace(priceText.Replace("₽", "").Replace(',', '.'), @"\s+", "");

            if (decimal.TryParse(cleanedString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                return result;

            return null;
        }
    }
}