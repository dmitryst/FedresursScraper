using Lots.Data;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Services;

// Используем WebApplicationBuilder для создания веб-приложения
var builder = WebApplication.CreateBuilder(args);

// Регистрация сервисов в DI контейнере
builder.Services.AddControllers(); // Добавляем поддержку API-контроллеров

var connectionString = builder.Configuration.GetConnectionString("Postgres");

// Регистрация DbContext
builder.Services.AddDbContext<LotsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Регистрация фабрики для создания ChromeDriver
builder.Services.AddSingleton<IWebDriverFactory, WebDriverFactory>();

// Регистрация парсеров
builder.Services.AddTransient<IBiddingScraper, BiddingScraper>();
builder.Services.AddTransient<ILotsScraper, LotsScraper>();

// Регистрация других сервисов
builder.Services.AddTransient<ICadastralNumberExtractor, CadastralNumberExtractor>();
builder.Services.AddTransient<IRosreestrService, RosreestrService>();
builder.Services.AddScoped<ILotCopyService, LotCopyService>();

// Регистрация фоновых сервисов
bool parsersEnabled = builder.Configuration.GetValue<bool>("BackgroundParsers:Enabled");

if (parsersEnabled)
{
    builder.Services.AddSingleton<ILotIdsCache, InMemoryLotIdsCache>();
    builder.Services.AddHostedService<BiddingIdsParser>();
    builder.Services.AddHostedService<BiddingWithLotsParser>();
}

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
                      policy  =>
                      {
                          // Разрешаем запросы от Next.js приложения
                          policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
                                // Разрешаем любые HTTP-методы (GET, POST, OPTIONS и т.д.)
                                .AllowAnyMethod()
                                // Разрешаем любые заголовки в запросе (Content-Type, Authorization и т.д.)
                                .AllowAnyHeader();
                      });
});

// Сборка приложения
var app = builder.Build();

// Применение миграций при старте (логика сохранена)
await ApplyMigrations(app);

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Приложение запущено.");

// Настройка HTTP-конвейера для обработки API-запросов
app.UseCors(myAllowSpecificOrigins);
app.UseAuthorization();
app.MapControllers(); // Включаем маппинг запросов на контроллеры

// Запуск приложения (и фоновых задач, и API)
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