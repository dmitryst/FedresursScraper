using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Lots.Data.Entities;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FedResursScraper
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                IConfiguration configuration = hostContext.Configuration;

                services.AddDbContext<LotsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
            })
            .Build();

            await ApplyMigrations(host);

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

                    // Вид торгов
                    string biddingType = "не найдено";
                    try
                    {
                        // Ищем элемент с текстом "Вид торгов"
                        var nameElement = driver.FindElement(By.XPath("//div[contains(text(),'Вид торгов')]"));
                        // Находим следующий за ним элемент, который содержит значение
                        var valueElement = nameElement.FindElement(By.XPath("./following-sibling::div"));
                        biddingType = valueElement.Text.Trim();
                    }
                    catch
                    {
                        // Оставляем "не найдено", если поле отсутствует на странице
                    }

                    // Категория
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

                    // Начальная цена
                    string startPriceText = "не найдено";
                    try
                    {
                        var nameElement = driver.FindElement(By.XPath("//div[contains(text(),'Начальная цена')]"));
                        var valueElement = nameElement.FindElement(By.XPath("./following-sibling::div"));
                        startPriceText = valueElement.Text.Trim();
                    }
                    catch { }
                    decimal? startPrice = ParsePrice(startPriceText);

                    // Шаг цены
                    string stepText = "не найдено";
                    try
                    {
                        var nameElement2 = driver.FindElement(By.XPath("//div[contains(text(),'Шаг аукциона')]"));
                        var valueElement2 = nameElement2.FindElement(By.XPath("./following-sibling::div"));
                        stepText = valueElement2.Text.Trim();
                    }
                    catch { }
                    decimal? step = ParsePrice(stepText);

                    // Задаток
                    string depositText = "не найдено";
                    try
                    {
                        var depositName = driver.FindElement(By.XPath("//div[contains(text(),'Задаток')]"));
                        var depositValue = depositName.FindElement(By.XPath("./following-sibling::div"));
                        depositText = depositValue.Text.Trim();
                    }
                    catch { }
                    decimal? deposit = ParsePrice(depositText);

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

                    allLots.Add(new LotInfo
                    {
                        BiddingType = biddingType,
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
                Console.WriteLine($"Начальная цена: {lot.StartPrice} ₽");
                Console.WriteLine($"Шаг цены:       {lot.Step} ₽");
                Console.WriteLine($"Задаток:        {lot.Deposit} ₽");
                Console.WriteLine($"Ознакомление:   {lot.ViewingProcedure}");
                Console.WriteLine($"Источник:       {lot.Url}");
                Console.WriteLine(new string('-', 40));
            }

            using (var scope = host.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                await SaveToDatabase(allLots, dbContext);
            }
        }

        private static async Task ApplyMigrations(IHost host)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var context = services.GetRequiredService<LotsDbContext>();
                    await context.Database.MigrateAsync();
                    Console.WriteLine("Миграции успешно применены.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Произошла ошибка при применении миграций: {ex.Message}");
                    return;
                }
            }
        }

        private static decimal? ParsePrice(string priceText)
        {
            if (string.IsNullOrWhiteSpace(priceText) || priceText.Equals("не найдено", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 1. Убираем символ валюты и лишние пробелы в начале/конце
            var cleanedString = priceText.Replace("₽", "").Trim();

            // 2. Убираем пробелы-разделители тысяч
            cleanedString = Regex.Replace(cleanedString, @"\s+", "");

            // 3. Заменяем запятую на точку для универсального парсинга
            cleanedString = cleanedString.Replace(',', '.');

            // 4. Безопасно парсим, используя инвариантную культуру
            if (decimal.TryParse(cleanedString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            return null; // Возвращаем null, если парсинг не удался
        }

        private static async Task SaveToDatabase(List<LotInfo> allLots, LotsDbContext db)
        {
            foreach (var lotInfo in allLots)
            {
                var lot = new Lot
                {
                    BiddingType = lotInfo.BiddingType,
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
            Console.WriteLine($"\nУспешно сохранено {allLots.Count} лотов в базу данных.");
        }
    }
}