using FedresursScraper.Services;
using Lots.Data;
using Microsoft.EntityFrameworkCore;

// 1. Используем WebApplicationBuilder для создания веб-приложения
var builder = WebApplication.CreateBuilder(args);

// 2. Регистрация сервисов в DI контейнере
builder.Services.AddControllers(); // Добавляем поддержку API-контроллеров

// Ваша логика для сборки строки подключения - она сохранена
var host = builder.Configuration["POSTGRES_HOST"];
var port = builder.Configuration["POSTGRES_PORT"];
var user = builder.Configuration["POSTGRES_USER"];
var password = builder.Configuration["POSTGRES_PASSWORD"];
var db = builder.Configuration["POSTGRES_DB"];
var connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={password}";

// Регистрация DbContext
builder.Services.AddDbContext<LotsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Регистрация фабрики для создания ChromeDriver
builder.Services.AddSingleton<IWebDriverFactory, ChromeDriverFactory>();

// Регистрация кэша для ID лотов
builder.Services.AddSingleton<ILotIdsCache, InMemoryLotIdsCache>();

// Регистрация сервиса для парсинга
builder.Services.AddTransient<IScraperService, ScraperService>();

// Регистрация фоновых сервисов
builder.Services.AddHostedService<LotIdsParser>();
builder.Services.AddHostedService<LotInfoParser>();

// 3. Сборка приложения
var app = builder.Build();

// 4. Применение миграций при старте (логика сохранена)
await ApplyMigrations(app);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Приложение запущено.");

// 5. Настройка HTTP-конвейера для обработки API-запросов
app.UseAuthorization();
app.MapControllers(); // Включаем маппинг запросов на контроллеры

// 6. Запуск приложения (и фоновых задач, и API)
await app.RunAsync();


// Вспомогательный метод для применения миграций
static async Task ApplyMigrations(WebApplication app)
{
    using (var scope = app.Services.CreateScope())
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