using OpenQA.Selenium;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FedResursScraper
{
    public class ScraperService : IScraperService
    {
        public async Task<LotInfo> ScrapeLotData(IWebDriver driver, string lotUrl)
        {
            driver.Navigate().GoToUrl(lotUrl);

            // ждем от 3 до 4.5 секунд пока прогрузится страница
            var random = new Random();
            await Task.Delay(3000 + random.Next(1500));

            // Выводим HTML страницы для анализа
            // var pageSource = driver.PageSource;
            // File.WriteAllText("debug.html", pageSource);

            // Парсинг всех полей с помощью универсального метода
            string biddingType = ParseField(driver, "Вид торгов");
            string startPriceText = ParseField(driver, "Начальная цена");
            string stepText = ParseField(driver, "Шаг аукциона");
            string depositText = ParseField(driver, "Задаток");

            var categories = ParseCategories(driver);
            var (description, viewingProcedure) = ParseAndSplitDescription(driver);

            return new LotInfo
            {
                Url = lotUrl,
                BiddingType = biddingType,
                Categories = categories,
                StartPrice = ParsePrice(startPriceText),
                Step = ParsePrice(stepText),
                Deposit = ParsePrice(depositText),
                Description = description,
                ViewingProcedure = viewingProcedure
            };
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