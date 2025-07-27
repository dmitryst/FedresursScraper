using OpenQA.Selenium.Chrome;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

                services.AddDbContext<LotsDbContext>(options =>
                    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

                services.AddTransient<ScraperService>();
            })
            .Build();

            await ApplyMigrations(host);

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Приложение запущено.");

            var allLots = new List<LotInfo>();
            var urlsToParse = await File.ReadAllLinesAsync("data.txt");

            var options = new ChromeOptions();
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);

            using (var driver = new ChromeDriver(options))
            {
                var scraperService = host.Services.GetRequiredService<ScraperService>();

                foreach (var lotUrl in urlsToParse)
                {
                    if (string.IsNullOrWhiteSpace(lotUrl)) continue;

                    try
                    {
                        logger.LogInformation("Обработка URL: {LotUrl}", lotUrl);

                        var lotInfo = await scraperService.ScrapeLotData(driver, lotUrl);
                        allLots.Add(lotInfo);
                    }
                    catch (Exception ex)
                    {
                        // Изолируем ошибку и логируем ее, чтобы не останавливать весь процесс
                        logger.LogError(ex, "Ошибка при обработке URL: {LotUrl}", lotUrl);
                    }
                }
            }

            // Сохраняем все собранные данные в БД
            using (var scope = host.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();
                await SaveToDatabase(allLots, dbContext, logger);
            }

            logger.LogInformation("Работа завершена. Обработано {LotCount} лотов.", allLots.Count);
        }

        private static async Task ApplyMigrations(IHost host)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();
                try
                {
                    var context = services.GetRequiredService<LotsDbContext>();
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Миграции успешно применены.");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Произошла критическая ошибка при применении миграций.");
                    // Важно остановить приложение, если миграции не применились
                    Environment.Exit(1);
                }
            }
        }

        private static async Task SaveToDatabase(List<LotInfo> allLots, LotsDbContext db, ILogger<Program> logger)
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

            try
            {
                await db.SaveChangesAsync();

                logger.LogInformation("Успешно сохранено {LotCount} лотов в базу данных.", allLots.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Произошла ошибка при сохранении данных в БД.");
            }
        }
    }
}