using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using System.Globalization;
using System.Text.RegularExpressions;
using FedresursScraper.Services.Models;

namespace FedresursScraper.Services
{
    /// <summary>
    /// Сервис парсинга сущности торгов с сайта fedresurs.ru
    /// </summary>
    public class BiddingScraper : IBiddingScraper
    {
        private readonly ILogger<IBiddingScraper> _logger;

        public BiddingScraper(ILogger<IBiddingScraper> logger)
        {
            _logger = logger;
        }

        public async Task<BiddingInfo> ScrapeBiddingInfoAsync(IWebDriver driver, Guid biddingId)
        {
            var url = $"https://fedresurs.ru/biddings/{biddingId}";

            driver.Navigate().GoToUrl(url);

            // ждем от 3 до 4.5 секунд пока прогрузится страница
            var random = new Random();
            await Task.Delay(3000 + random.Next(1500));

            // Для дебага: выводим HTML страницы для анализа
            // var pageSource = driver.PageSource;
            // File.WriteAllText("debug.html", pageSource);

            var biddingType = ParseSimpleField(driver, "Вид торгов");
            var bidAcceptancePeriod = ParseSimpleField(driver, "Прием заявок");
            var tradePeriod = ParseSimpleField(driver, "Период торгов");
            var organizer = ParseSimpleField(driver, "Организатор торгов");

            var announcementRawText = ParseSimpleField(driver, "Объявление о торгах");
            DateTime? announcementDate = ParseDateTime(announcementRawText);

            var resultsDateRaw = ParseSimpleField(driver, "Дата объявления результатов");
            DateTime? resultsDate = ParseDateTime(resultsDateRaw);

            var (_, viewingProcedure) = ParseAndSplitDescription(driver);

            var debtorInfo = ParseSubjectBlock(driver, "Должник");
            var managerInfo = ParseSubjectBlock(driver, "Арбитражный управляющий");
            var legalCaseInfo = ParseLegalCase(driver);

            return new BiddingInfo
            {
                Id = biddingId,
                AnnouncedAt = announcementDate,
                Type = biddingType ?? "Не определен",

                BidAcceptancePeriod = bidAcceptancePeriod,
                TradePeriod = tradePeriod,
                ResultsAnnouncementDate = resultsDate,

                BankruptMessageId = ParseBankruptMessageId(driver),

                Organizer = organizer,

                DebtorId = debtorInfo.Id,
                DebtorName = debtorInfo.Name,
                DebtorInn = debtorInfo.Inn,
                DebtorSnils = debtorInfo.Snils,
                DebtorOgrn = debtorInfo.Ogrn,
                IsDebtorCompany = debtorInfo.IsCompany,

                ArbitrationManagerId = managerInfo.Id,
                ArbitrationManagerName = managerInfo.Name,
                ArbitrationManagerInn = managerInfo.Inn,

                LegalCaseId = legalCaseInfo.Id,
                LegalCaseNumber = legalCaseInfo.Number,

                ViewingProcedure = viewingProcedure,
            };
        }

        private record LegalCaseParseResult(Guid? Id, string? Number);
        private record SubjectParseResult(Guid? Id, string? Name, string? Inn, string? Snils, string? Ogrn, bool IsCompany);

        private SubjectParseResult ParseSubjectBlock(IWebDriver driver, string label)
        {
            try
            {
                // Ищем контейнер с заголовком (например, "Должник" или "Арбитражный управляющий")
                // Используем XPath для поиска по тексту заголовка
                var labelElements = driver.FindElements(By.XPath($"//div[contains(@class, 'info-item-name') and contains(text(), '{label}')]"));

                if (!labelElements.Any())
                    return new SubjectParseResult(null, null, null, null, null, false);

                // Получаем соседний div со значением (info-item-value)
                var valueDiv = labelElements.First().FindElement(By.XPath("./following-sibling::div[contains(@class, 'info-item-value')]"));

                // Пытаемся определить ссылку и имя
                string? name = null;
                Guid? id = null;
                string? href = null;

                try
                {
                    // Ссылка может быть внутри .company-name
                    var linkElement = valueDiv.FindElement(By.XPath(".//div[contains(@class, 'company-name')]//a"));
                    name = linkElement.Text.Trim();
                    href = linkElement.GetAttribute("href");
                    id = ExtractGuidFromHref(href);
                }
                catch
                {
                    // Если ссылки нет, берем просто текст
                    name = valueDiv.Text.Trim();
                }

                // Определяем тип (Компания или Физлицо) по ссылке
                bool isCompany = href != null && href.Contains("/companies/");

                // Парсим идентификаторы (ИНН, СНИЛС, ОГРН)
                // Они лежат в блоках .company-identifier-item
                string? inn = ParseSubField(valueDiv, "ИНН");
                string? snils = ParseSubField(valueDiv, "СНИЛС");
                string? ogrn = ParseSubField(valueDiv, "ОГРН");

                return new SubjectParseResult(id, name, inn, snils, ogrn, isCompany);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при парсинге блока '{Label}'", label);
                return new SubjectParseResult(null, null, null, null, null, false);
            }
        }

        private string? ParseSubField(IWebElement parent, string subLabel)
        {
            try
            {
                var els = parent.FindElements(By.XPath($".//div[contains(@class, 'company-identifier-item-name') and contains(text(), '{subLabel}')]/following-sibling::div"));
                if (els.Any()) return els.First().Text.Trim();
            }
            catch { }
            return null;
        }

        private LegalCaseParseResult ParseLegalCase(IWebDriver driver)
        {
            try
            {
                var labelElements = driver.FindElements(By.XPath("//div[contains(text(), 'Номер дела')]"));
                if (!labelElements.Any()) return new LegalCaseParseResult(null, null);

                var valueDiv = labelElements.First().FindElement(By.XPath("./following-sibling::div"));
                var link = valueDiv.FindElement(By.TagName("a"));

                string number = link.Text.Trim();
                Guid? id = ExtractGuidFromHref(link.GetAttribute("href"));

                return new LegalCaseParseResult(id, number);
            }
            catch
            {
                return new LegalCaseParseResult(null, null);
            }
        }

        private Guid? ExtractGuidFromHref(string? href)
        {
            if (string.IsNullOrEmpty(href)) return null;
            var segments = href.Split('/');
            var guidString = segments.LastOrDefault();
            if (Guid.TryParse(guidString, out Guid result)) return result;
            return null;
        }

        private Guid? ParseBankruptMessageId(IWebDriver driver)
        {
            try
            {
                // Находим ссылку внутри блока с объявлением о торгах
                var linkElement = driver.FindElement(By.XPath("//div[contains(text(),'Объявление о торгах')]/following-sibling::div//a"));
                return ExtractGuidFromHref(linkElement.GetAttribute("href"));
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

        private DateTime? ParseDateTime(string? rawText)
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

                        // Конвертируем "неопределенное" время в UTC, считая, что исходник был в MSK.
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

        /// <summary>
        /// Универсальный метод для парсинга полей "ключ-значение"
        /// </summary>
        /// <param name="driver"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        private string? ParseSimpleField(IWebDriver driver, string fieldName)
        {
            try
            {
                // Используем FindElements, чтобы избежать исключения, если элемента нет
                var xpath = $"//div[contains(text(),'{fieldName}')]/following-sibling::div";
                var elements = driver.FindElements(By.XPath(xpath));
                if (elements.Any())
                {
                    return elements.First().Text.Trim();
                }
            }
            catch (Exception)
            {
                // Можно добавить логирование, если нужно
            }
            return null;
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
    }
}