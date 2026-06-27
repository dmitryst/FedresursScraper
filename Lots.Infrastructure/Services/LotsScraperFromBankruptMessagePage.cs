using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using System.Globalization;
using System.Text.RegularExpressions;
using FedresursScraper.Services.Models;

namespace FedresursScraper.Services
{
    /// <summary>
    /// Сервис для парсинга лотов со страницы сообщения о торгах.
    /// </summary>
    public class LotsScraperFromBankruptMessagePage : ILotsScraperFromBankruptMessagePage
    {
        private readonly ILogger<LotsScraperFromBankruptMessagePage> _logger;
        private readonly ICadastralNumberExtractor _cadastralNumberExtractor;

        public LotsScraperFromBankruptMessagePage(
            ILogger<LotsScraperFromBankruptMessagePage> logger,
            ICadastralNumberExtractor cadastralNumberExtractor)
        {
            _logger = logger;
            _cadastralNumberExtractor = cadastralNumberExtractor;
        }

        public async Task<BankruptMessageScrapeResult> ScrapeAsync(IWebDriver driver, Guid messageId)
        {
            var result = new BankruptMessageScrapeResult();
            var url = $"https://fedresurs.ru/bankruptmessages/{messageId}";

            try
            {
                driver.Navigate().GoToUrl(url);
                await Task.Delay(3000 + new Random().Next(1500));

                result.Attachments = await FetchAttachmentsFromApiAsync(messageId, url);
                if (result.Attachments.Count == 0)
                {
                    _logger.LogWarning(
                        "API Федресурса не вернул вложения для сообщения {MessageId}, пробуем DOM.",
                        messageId);
                    result.Attachments = ParseAttachments(driver);
                }

                result.Lots = ParseLots(driver, url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла ошибка при парсинге страницы {Url}", url);
            }

            return result;
        }

        private List<LotInfo> ParseLots(IWebDriver driver, string url)
        {
            var lots = new List<LotInfo>();
            var lotRows = driver.FindElements(By.CssSelector(".message-table tbody tr"));

            if (!lotRows.Any())
            {
                _logger.LogWarning("На странице {Url} не найдена таблица с лотами (.message-table tbody tr).", url);
                return lots;
            }

            _logger.LogInformation("Найдено {LotCount} строк с лотами для парсинга.", lotRows.Count);

            foreach (var row in lotRows)
            {
                var cells = row.FindElements(By.TagName("td"));

                if (cells.Count < 3)
                {
                    _logger.LogWarning("В строке таблицы лотов обнаружено некорректное количество ячеек ({CellCount}). Ожидалось 3.", cells.Count);
                    continue;
                }

                var numberCell = cells[0];
                var detailsCell = cells[1];
                var priceCell = cells[2];

                string startPriceRaw = GetTextByLabel(priceCell, "Начальная цена");
                decimal? startPrice = ParsePrice(startPriceRaw);

                string stepRaw = GetTextByLabel(priceCell, "Шаг аукциона");
                string depositRaw = GetTextByLabel(priceCell, "Задаток");

                var description = ParseLotDescription(detailsCell);
                var cadastralNumbers = _cadastralNumberExtractor.Extract(description);

                lots.Add(new LotInfo
                {
                    Number = numberCell.Text.Trim(),
                    Description = description,
                    Categories = [],
                    StartPrice = startPrice,
                    Step = ParseFinancialValue(stepRaw, startPrice),
                    Deposit = ParseFinancialValue(depositRaw, startPrice),
                    CadastralNumbers = cadastralNumbers,
                });
            }

            return lots;
        }

        private async Task<List<FedresursAttachmentInfo>> FetchAttachmentsFromApiAsync(Guid messageId, string referer)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", referer);

                var apiUrl = FedresursBankruptMessageDocsParser.BuildMessageApiUrl(messageId);
                var json = await client.GetStringAsync(apiUrl);
                var attachments = FedresursBankruptMessageDocsParser.ParseAttachmentsFromApiJson(json);

                _logger.LogInformation(
                    "Из API Федресурса получено {Count} вложений для сообщения {MessageId}.",
                    attachments.Count,
                    messageId);

                return attachments;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось получить вложения из API Федресурса для сообщения {MessageId}", messageId);
                return [];
            }
        }

        internal static List<FedresursAttachmentInfo> ParseAttachments(IWebDriver driver)
        {
            var attachments = new List<FedresursAttachmentInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var link in FindTradeDocumentLinks(driver))
            {
                var href = link.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    continue;

                var title = ExtractLinkTitle(link);
                var extension = LotPropertyDocumentHelper.DetectDocumentExtension(href, title);
                if (extension == null)
                    continue;

                var absoluteUrl = ToAbsoluteUrl(href);
                if (!LotPropertyDocumentHelper.IsLikelyTradeDocumentLink(title, absoluteUrl, extension))
                    continue;

                if (!seen.Add(absoluteUrl))
                    continue;

                if (string.IsNullOrWhiteSpace(title))
                    title = Path.GetFileName(new Uri(absoluteUrl).LocalPath);

                attachments.Add(new FedresursAttachmentInfo
                {
                    Title = title,
                    Url = absoluteUrl,
                    Extension = extension,
                });
            }

            return attachments;
        }

        internal static IEnumerable<IWebElement> FindTradeDocumentLinks(IWebDriver driver)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<IWebElement>();

            void AddLinks(IEnumerable<IWebElement> links)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttribute("href");
                    if (string.IsNullOrWhiteSpace(href) || !seen.Add(href))
                        continue;

                    results.Add(link);
                }
            }

            // Блок «Документы» под таблицей лотов.
            AddLinks(driver.FindElements(By.XPath(
                "//table[contains(@class,'message-table')]/following-sibling::*" +
                "//a[@href and not(ancestor::table[contains(@class,'message-table')])]")));

            if (results.Count > 0)
                return results;

            // Заголовок «Документы» и соседний контейнер.
            foreach (var header in driver.FindElements(By.XPath(
                         "//*[normalize-space(text())='Документы' or normalize-space(text())='Документы:']")))
            {
                try
                {
                    var section = header.FindElement(By.XPath(
                        "./ancestor::div[contains(@class,'message')][1] | ./parent::*"));
                    AddLinks(section.FindElements(By.CssSelector("a[href]")));
                }
                catch (NoSuchElementException)
                {
                    // skip
                }
            }

            if (results.Count > 0)
                return results;

            AddLinks(driver.FindElements(By.CssSelector(
                ".message-documents a[href], .message-files a[href], .message-attachments a[href]")));

            return results;
        }

        private static string ExtractLinkTitle(IWebElement link)
        {
            var title = link.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            title = link.GetAttribute("title")?.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            try
            {
                var parentText = link.FindElement(By.XPath("..")).Text?.Trim();
                if (!string.IsNullOrWhiteSpace(parentText))
                    return parentText;
            }
            catch (NoSuchElementException)
            {
                // ignore
            }

            return string.Empty;
        }

        private static string ToAbsoluteUrl(string href)
        {
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return href;

            if (href.StartsWith('/'))
                return $"https://fedresurs.ru{href}";

            return $"https://fedresurs.ru/{href}";
        }

        private string GetTextByLabel(IWebElement parentCell, string label)
        {
            try
            {
                var items = parentCell.FindElements(By.CssSelector(".td-inner-item"));
                foreach (var item in items)
                {
                    var labelElement = item.FindElements(By.CssSelector(".fw-light")).FirstOrDefault();
                    if (labelElement != null && labelElement.Text.Trim().Contains(label))
                    {
                        var valueElements = item.FindElements(By.TagName("div"));
                        if (valueElements.Count > 1)
                        {
                            return valueElements[1].Text.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось найти значение для метки '{Label}'.", label);
            }
            return "не найдено";
        }

        private string ParseLotDescription(IWebElement detailsCell)
        {
            try
            {
                var items = detailsCell.FindElements(By.CssSelector(".td-inner-item"));
                foreach (var item in items)
                {
                    var labelElement = item.FindElements(By.CssSelector(".fw-light")).FirstOrDefault();
                    if (labelElement != null && labelElement.Text.Trim() == "Описание")
                    {
                        return item.Text.Replace(labelElement.Text, "").Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось распарсить описание лота.");
            }
            return "не найдено";
        }

        private decimal? ParseFinancialValue(string rawValue, decimal? basePrice)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
                return null;

            if (rawValue.Contains('%'))
            {
                var cleanedPercentString = Regex.Replace(rawValue.Replace('%', ' ').Replace(',', '.'), @"\s+", "");
                if (decimal.TryParse(cleanedPercentString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal percentage))
                {
                    if (basePrice.HasValue)
                    {
                        return Math.Round((basePrice.Value * percentage) / 100, 2);
                    }

                    _logger.LogWarning("Невозможно рассчитать процент '{RawValue}', так как начальная цена не найдена.", rawValue);
                    return null;
                }
            }

            return ParsePrice(rawValue);
        }

        private decimal? ParsePrice(string priceText)
        {
            if (string.IsNullOrWhiteSpace(priceText) || priceText.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
                return null;

            var cleanedString = Regex.Replace(priceText.Replace("₽", "").Replace(',', '.'), @"\s+", "");
            if (decimal.TryParse(cleanedString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                return result;

            _logger.LogWarning("Не удалось распарсить цену из строки: '{PriceText}'", priceText);
            return null;
        }
    }
}
