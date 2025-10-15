using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.RegularExpressions;
using FedresursScraper.Services.Models;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FedresursScraper.Services
{
    /// <summary>
    /// Сервис для парсинга лотов со страницы сообщения о торгах.
    /// </summary>
    public class LotsScraper : ILotsScraper
    {
        private readonly ILogger<LotsScraper> _logger;
        private readonly ICadastralNumberExtractor _cadastralNumberExtractor;
        private readonly IRosreestrService _rosreestrService;
        private readonly IBackgroundTaskQueue _taskQueue;

        public LotsScraper(
            ILogger<LotsScraper> logger,
            ICadastralNumberExtractor cadastralNumberExtractor,
            IRosreestrService rosreestrService,
            IBackgroundTaskQueue taskQueue)
        {
            _logger = logger;
            _cadastralNumberExtractor = cadastralNumberExtractor;
            _rosreestrService = rosreestrService;
            _taskQueue = taskQueue;
        }

        /// <summary>
        /// Асинхронно парсит все лоты со страницы сообщения.
        /// </summary>
        /// <param name="driver">Экземпляр IWebDriver для управления браузером.</param>
        /// <param name="messageId">GUID сообщения о торгах.</param>
        /// <returns>Список объектов LotInfo.</returns>
        public async Task<List<LotInfo>> ScrapeLotsAsync(IWebDriver driver, Guid messageId)
        {
            var lots = new List<LotInfo>();
            var url = $"https://fedresurs.ru/bankruptmessages/{messageId}";

            try
            {
                driver.Navigate().GoToUrl(url);
                await Task.Delay(3000 + new Random().Next(1500));

                // var pageSource = driver.PageSource;

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

                    // В новой структуре 3 основные ячейки: Номер, Лот, Информация о цене
                    if (cells.Count < 3)
                    {
                        _logger.LogWarning("В строке таблицы лотов обнаружено некорректное количество ячеек ({CellCount}). Ожидалось 3.", cells.Count);
                        continue;
                    }

                    var numberCell = cells[0];
                    var detailsCell = cells[1]; // Ячейка с описанием и классификатором
                    var priceCell = cells[2];   // Ячейка со всеми ценами

                    // Сначала получаем начальную цену, т.к. она нужна для расчета процентов
                    string startPriceRaw = GetTextByLabel(priceCell, "Начальная цена");
                    decimal? startPrice = ParsePrice(startPriceRaw);

                    string stepRaw = GetTextByLabel(priceCell, "Шаг аукциона");
                    string depositRaw = GetTextByLabel(priceCell, "Задаток");

                    var description = ParseLotDescription(detailsCell);
                    var cadastralNumbers = _cadastralNumberExtractor.Extract(description);
                    var coordinates = await _rosreestrService.FindFirstCoordinatesAsync(cadastralNumbers);

                    var lotInfo = new LotInfo
                    {
                        Number = numberCell.Text.Trim(),
                        Description = description,
                        Categories = [],
                        StartPrice = startPrice,
                        Step = ParseFinancialValue(stepRaw, startPrice),
                        Deposit = ParseFinancialValue(depositRaw, startPrice),
                        CadastralNumbers = cadastralNumbers,
                        Latitude = coordinates?.Latitude,
                        Longitude = coordinates?.Longitude
                    };

                    lots.Add(lotInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла ошибка при парсинге лотов со страницы {Url}", url);
            }

            return lots;
        }

        /// <summary>
        /// Извлекает текст из вложенного блока по его заголовку (например, "Начальная цена").
        /// </summary>
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
                        // Значение обычно находится во втором div-элементе внутри .td-inner-item
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

        /// <summary>
        /// Извлекает чистое описание лота из ячейки.
        /// </summary>
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
                        // Полный текст блока минус текст заголовка
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

        /// <summary>
        /// Парсит денежное значение, которое может быть абсолютным или в процентах.
        /// </summary>
        private decimal? ParseFinancialValue(string rawValue, decimal? basePrice)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
                return null;

            // Обработка процентных значений
            if (rawValue.Contains('%'))
            {
                var cleanedPercentString = Regex.Replace(rawValue.Replace('%', ' ').Replace(',', '.'), @"\s+", "");
                if (decimal.TryParse(cleanedPercentString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal percentage))
                {
                    if (basePrice.HasValue)
                    {
                        return Math.Round((basePrice.Value * percentage) / 100, 2);
                    }
                    else
                    {
                        _logger.LogWarning("Невозможно рассчитать процент '{RawValue}', так как начальная цена не найдена.", rawValue);
                        return null;
                    }
                }
            }
            // Обработка абсолютных значений
            return ParsePrice(rawValue);
        }

        /// <summary>
        /// Парсит абсолютное денежное значение из строки.
        /// </summary>
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