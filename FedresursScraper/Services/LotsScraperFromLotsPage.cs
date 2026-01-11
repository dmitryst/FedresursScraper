using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.RegularExpressions;
using FedresursScraper.Services.Models;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace FedresursScraper.Services
{
    /// <summary>
    /// Сервис для парсинга лотов со страницы торгов.
    /// </summary>
    public interface ILotsScraperFromLotsPage
    {
        Task<List<LotInfo>> ScrapeLotsAsync(IWebDriver driver, Guid biddingId);
    }

    /// <inheritdoc>
    public class LotsScraperFromLotsPage : ILotsScraperFromLotsPage
    {
        private readonly ILogger<LotsScraperFromLotsPage> _logger;
        private readonly ICadastralNumberExtractor _cadastralNumberExtractor;

        public LotsScraperFromLotsPage(
            ILogger<LotsScraperFromLotsPage> logger,
            ICadastralNumberExtractor cadastralNumberExtractor)
        {
            _logger = logger;
            _cadastralNumberExtractor = cadastralNumberExtractor;
        }

        public async Task<List<LotInfo>> ScrapeLotsAsync(IWebDriver driver, Guid biddingId)
        {
            var lots = new List<LotInfo>();
            var url = $"https://fedresurs.ru/biddings/{biddingId}/lots";

            try
            {
                driver.Navigate().GoToUrl(url);

                try
                {
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                    // Ждем, пока появится хотя бы один элемент лота
                    // Используем тот же селектор, что и при поиске элементов ниже
                    wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("bidding-lot-card")));
                }
                catch (WebDriverTimeoutException)
                {
                    // Если за 10 секунд ничего не появилось, возможно, лотов нет или страница пустая.
                    // Логируем предупреждение, но не роняем программу сразу, 
                    // так как код ниже проверит count и корректно обработает пустой список.
                    _logger.LogWarning("Лоты не появились на странице за 10 секунд (biddingId: {Id})", biddingId);
                }
                // Загружаем все страницы (кликаем "Загрузить еще" пока можно)
                await LoadAllPagesAsync(driver);

                // Ищем контейнеры лотов
                // Используем точный тег bidding-lot-card, найденный при анализе HTML
                var lotCards = driver.FindElements(By.CssSelector("bidding-lot-card"));

                // Фолбек для старой верстки или если тег изменится
                if (!lotCards.Any())
                {
                    lotCards = driver.FindElements(By.CssSelector(".u-card-result.lot-item"));
                }

                foreach (var card in lotCards)
                {
                    try
                    {
                        // --- ПАРСИНГ ДАННЫХ В ПЕРЕМЕННЫЕ ---

                        // Номер лота
                        string number = GetTextSafe(card, By.CssSelector(".lot-item-header-name"))
                            ?.Replace("Лот №", "")
                            .Trim() ?? string.Empty;

                        // Цены (Начальная, Шаг, Задаток)
                        // Цены лежат внутри блока .lot-item-description -> .info-item
                        decimal? startPrice = null;
                        decimal? step = null;
                        decimal? deposit = null;

                        var infoItems = card.FindElements(By.CssSelector(".lot-item-description .info-item"));
                        foreach (var item in infoItems)
                        {
                            var label = GetTextSafe(item, By.CssSelector(".info-item-name"))?.ToLower() ?? "";
                            var value = GetTextSafe(item, By.CssSelector(".info-item-value"));

                            if (string.IsNullOrWhiteSpace(value)) continue;

                            if (label.Contains("начальная цена"))
                                startPrice = ParseMoneyString(value);
                            else if (label.Contains("шаг"))
                                step = ParseMoneyString(value);
                            else if (label.Contains("задаток"))
                                deposit = ParseMoneyString(value);
                        }

                        // Описание
                        // Реальное описание лежит в .lot-item-tradeobject (а не в description)
                        string rawDescription = GetTextSafe(card, By.CssSelector(".lot-item-tradeobject")) ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(rawDescription))
                        {
                            // Фолбек: иногда описание в другом блоке
                            rawDescription = GetTextSafe(card, By.CssSelector(".lot-item-description-text")) ?? string.Empty;
                        }

                        // Очистка описания (удаление "Показать еще" и порядка ознакомления)
                        string description = CleanDescription(rawDescription);

                        // Кадастровые номера (извлекаем из описания)
                        var cadastralNumbers = _cadastralNumberExtractor.Extract(description);

                        // --- СОЗДАНИЕ ОБЪЕКТА ---

                        var lot = new LotInfo
                        {
                            Number = number,
                            Description = description,
                            StartPrice = startPrice,
                            Step = step,
                            Deposit = deposit,
                            Categories = [],
                            CadastralNumbers = cadastralNumbers,
                        };

                        lots.Add(lot);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при парсинге одного из лотов.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка при парсинге лотов со страницы {Url}", url);
            }

            return lots;
        }

        /// <summary>
        /// Очищает описание от технических фраз ("Показать еще") и блока "Порядок ознакомления".
        /// Логика разделения заимствована из BiddingScraper.
        /// </summary>
        private string CleanDescription(string rawDesc)
        {
            if (string.IsNullOrWhiteSpace(rawDesc)) return string.Empty;

            // Удаляем "Показать еще" в конце, если есть (артефакт UI аккордеона)
            string showMoreMarker = "Показать еще";
            if (rawDesc.EndsWith(showMoreMarker, StringComparison.OrdinalIgnoreCase))
            {
                rawDesc = rawDesc.Substring(0, rawDesc.Length - showMoreMarker.Length).Trim();
            }

            // Логика из BiddingScraper: ищем маркеры начала порядка ознакомления

            // Маркер 1: "Порядок ознакомления с имуществом должника:"
            string marker1 = "Порядок ознакомления с имуществом должника:";
            int idx1 = rawDesc.IndexOf(marker1, StringComparison.OrdinalIgnoreCase);

            if (idx1 >= 0)
            {
                return rawDesc.Substring(0, idx1).Trim();
            }

            // Маркер 2: "С имуществом можно ознакомиться"
            string marker2 = "С имуществом можно ознакомиться";
            int idx2 = rawDesc.IndexOf(marker2, StringComparison.OrdinalIgnoreCase);

            if (idx2 >= 0)
            {
                return rawDesc.Substring(0, idx2).Trim();
            }

            // Если маркеры не найдены, возвращаем текст (уже без "Показать еще")
            return rawDesc;
        }

        /// <summary>
        /// Прокликивает кнопку "Загрузить еще" до тех пор, пока она не исчезнет
        /// </summary>
        private async Task LoadAllPagesAsync(IWebDriver driver)
        {
            int pageCount = 1;
            while (true)
            {
                try
                {
                    // Ищем кнопку .more_btn
                    var buttons = driver.FindElements(By.CssSelector(".more_btn"));

                    // Проверяем наличие и видимость
                    if (buttons.Count > 0 && buttons[0].Displayed && buttons[0].Enabled)
                    {
                        _logger.LogInformation("Найдена кнопка 'Загрузить еще' (стр. {Page}). Подгружаем данные...", pageCount);

                        // Клик через JS надежнее перекрытий
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", buttons[0]);

                        // Пауза для подгрузки контента
                        await Task.Delay(3000);
                        pageCount++;
                    }
                    else
                    {
                        // Кнопки нет — значит все загрузилось
                        _logger.LogInformation("Все страницы загружены. Кнопка 'Загрузить еще' скрыта.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Прерывание загрузки страниц: {Message}", ex.Message);
                    break;
                }
            }
        }

        /// <summary>
        /// Безопасное получение текста элемента по селектору
        /// </summary>
        private string? GetTextSafe(ISearchContext context, By by)
        {
            try
            {
                var element = context.FindElement(by);
                return element.Text.Trim();
            }
            catch (NoSuchElementException)
            {
                return null;
            }
        }

        /// <summary>
        /// Парсит денежную строку, убирая пробелы и валюту
        /// </summary>
        private decimal? ParseMoneyString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Убираем все лишнее, оставляем цифры, запятые и точки
            // Пример: "5 819,82 ₽" -> "5819.82"

            var clean = raw.Replace("руб", "", StringComparison.OrdinalIgnoreCase)
                           .Replace("₽", "")
                           .Replace("\u00A0", "") // Неразрывный пробел
                           .Replace(" ", "");

            // Обработка формата с запятой (RU)
            clean = clean.Replace(",", ".");

            // Если точек больше одной (разделители тысяч 1.000.000.00), удаляем все кроме последней
            if (clean.Count(c => c == '.') > 1)
            {
                var lastDot = clean.LastIndexOf('.');
                clean = clean.Substring(0, lastDot).Replace(".", "") + clean.Substring(lastDot);
            }

            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return null;
        }
    }
}