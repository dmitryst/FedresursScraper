using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Lots.Data.Entities;

namespace FedResursScraper
{
    public class Program
    {
        public static async Task Main()
        {
            // Путь к файлу со ссылками
            var filePath = "B:\\Т\\FedresursScraper\\FedresursScraper\\data.txt";

            // Проверка на существование файла
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Ошибка: Файл не найден по пути '{filePath}'.");
                Console.WriteLine("Убедитесь, что файл находится в одной папке с программой.");
                return;
            }

            // Чтение всех ссылок из файла
            var urlsToParse = await File.ReadAllLinesAsync(filePath);
            var allLots = new List<LotInfo>();

            Console.WriteLine($"Начинается обработка {urlsToParse.Length} ссылок из файла...");

            var options = new ChromeOptions();
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);

            using (var driver = new ChromeDriver(options))
            {
                // Перебор каждой ссылки из файла
                foreach (var lotUrl in urlsToParse)
                {
                    driver.Navigate().GoToUrl(lotUrl);

                    // Даем странице подгрузиться с разным интервалом ожидания, чтобы не обнаружили, что мы бот
                    var random = new Random();
                    double delaySeconds = 3.0 + random.NextDouble() * 2.0; // диапазон от 3.0 до 5.0
                    int delayMilliseconds = (int)(delaySeconds * 1000);
                    await Task.Delay(delayMilliseconds);

                    // Выводим HTML страницы для анализа
                    var pageSource = driver.PageSource;
                    // File.WriteAllText("debug.html", pageSource);

                    // 0. Категория
                    List<string> categories = new List<string>();
                    try
                    {
                        var categoryElements = driver.FindElements(By.CssSelector(".lot-item-classifiers .lot-item-classifiers-element"));
                        foreach (var el in categoryElements)
                        {
                            var text = el.Text.Trim();
                            if (!string.IsNullOrEmpty(text))
                                categories.Add(text);
                        }
                    }
                    catch
                    {
                        // Если блок полностью отсутствует — categories останется пустым
                    }

                    // 1. Начальная цена
                    string startPrice = "не найдено";
                    try
                    {
                        var nameElement = driver.FindElement(By.XPath("//div[contains(text(),'Начальная цена')]"));
                        var valueElement = nameElement.FindElement(By.XPath("./following-sibling::div"));
                        startPrice = valueElement.Text.Trim();
                    }
                    catch { }

                    // 2. Шаг аукциона
                    string step = "не найдено";
                    try
                    {
                        var nameElement2 = driver.FindElement(By.XPath("//div[contains(text(),'Шаг аукциона')]"));
                        var valueElement2 = nameElement2.FindElement(By.XPath("./following-sibling::div"));
                        step = valueElement2.Text.Trim();
                    }
                    catch { }

                    // 3. Задаток
                    string deposit = "не найдено";
                    try
                    {
                        var depositName = driver.FindElement(By.XPath("//div[contains(text(),'Задаток')]"));
                        var depositValue = depositName.FindElement(By.XPath("./following-sibling::div"));
                        deposit = depositValue.Text.Trim();
                    }
                    catch { }

                    // 4. Описание объекта
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

                    // 5. Порядок ознакомления с имуществом должника – гибкая логика по двум маркерам
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

                    allLots.Add(new LotInfo
                    {
                        Categories = categories,
                        Description = description,
                        StartPrice = startPrice,
                        Step = step,
                        Deposit = deposit,
                        ViewingProcedure = viewingProcedure,
                        Url = lotUrl,
                    });
                }
            }

            //Вывод результатов
            Console.WriteLine("\n-------------------------------------------");
            Console.WriteLine($"Обработка завершена. Всего лотов: {allLots.Count}");
            Console.WriteLine("-------------------------------------------");

            foreach (var lot in allLots)
            {
                Console.WriteLine($"Категория:      {string.Join("; ", lot.Categories)}");
                Console.WriteLine($"Наименование:   {lot.Description}");
                Console.WriteLine($"Цена:           {lot.StartPrice}");
                Console.WriteLine($"Шаг цены:       {lot.Step}");
                Console.WriteLine($"Задаток:        {lot.Deposit}");
                Console.WriteLine($"Ознакомление:   {lot.ViewingProcedure}");
                Console.WriteLine($"Источник:       {lot.Url}");
                Console.WriteLine(new string('-', 40));
            }

            await SaveToDatabase(allLots);
        }

        private static async Task SaveToDatabase(List<LotInfo> allLots)
        {
            using (var db = new LotsDbContext())
            {
                foreach (var lotInfo in allLots)
                {
                    var lot = new Lot
                    {
                        Url = lotInfo.Url,
                        StartPrice = lotInfo.StartPrice,
                        Step = lotInfo.Step,
                        Deposit = lotInfo.Deposit,
                        Description = lotInfo.Description,
                        ViewingProcedure = lotInfo.ViewingProcedure
                    };

                    // Записываем все категории, исключая пустые
                    foreach (var cat in lotInfo.Categories)
                    {
                        if (!string.IsNullOrWhiteSpace(cat))
                            lot.Categories.Add(new LotCategory { Name = cat });
                    }

                    db.Lots.Add(lot);
                }
                await db.SaveChangesAsync();
            }
        }
    }
}