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

                var host = configuration["POSTGRES_HOST"];
                var user = configuration["POSTGRES_USER"];
                var password = configuration["POSTGRES_PASSWORD"];
                var db = configuration["POSTGRES_DB"];

                var connectionString = $"Host={host};Port=5432;Database={db};Username={user};Password={password}";

                services.AddDbContext<LotsDbContext>(options =>
                    options.UseNpgsql(connectionString));

                services.AddTransient<ScraperService>();
                services.AddSingleton<ILotIdsCache, InMemoryLotIdsCache>();
                services.AddSingleton<IWebDriverFactory, WebDriverFactory>();
                services.AddHostedService<LotIdsParser>();
                services.AddHostedService<LotInfoParser>();
            })
            .Build();

            await ApplyMigrations(host);

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Приложение запущено.");  

            await host.RunAsync();   
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
    }
}