using Lots.Data;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// Используем WebApplicationBuilder для создания веб-приложения
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Регистрация сервисов в DI контейнере
builder.Services.AddControllers(); // Добавляем поддержку API-контроллеров

var connectionString = configuration.GetConnectionString("Postgres");

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
// builder.Services.AddTransient<IRosreestrService, RosreestrService>();
builder.Services.AddScoped<ILotCopyService, LotCopyService>();

// Регистрация фоновых сервисов
bool parsersEnabled = configuration.GetValue<bool>("BackgroundParsers:Enabled");

if (parsersEnabled)
{
    builder.Services.AddSingleton<ILotIdsCache, InMemoryLotIdsCache>();
    builder.Services.AddHostedService<BiddingIdsParser>();
    builder.Services.AddHostedService<BiddingWithLotsParser>();
    builder.Services.AddHostedService<LotClassificationService>();
}

builder.Services.AddSingleton<ILotClassifier>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<LotClassifier>>();

    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string apiKey = configuration["DeepSeek:ApiKey"] ??
        throw new InvalidOperationException("API ключ для DeepSeek не найден в конфигурации (DeepSeek:ApiKey).");

    return new LotClassifier(logger, apiKey);
});

builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

var rosreestrServiceUrl = Environment.GetEnvironmentVariable("ROSREESTR_SERVICE_URL");

if (string.IsNullOrWhiteSpace(rosreestrServiceUrl))
{
    // Можно выбросить исключение или установить значение по умолчанию для локальной разработки
    throw new InvalidOperationException("Переменная окружения ROSREESTR_SERVICE_URL не установлена.");
    // или: rosreestrServiceUrl = "http://localhost:8000"; // для локальной отладки
}

// Регистрируем типизированный HttpClient
builder.Services.AddHttpClient<IRosreestrServiceClient, RosreestrServiceClient>(client =>
{
    client.BaseAddress = new Uri(rosreestrServiceUrl);
});

// настройка аутентификации
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = configuration["Jwt:Audience"],
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"])),
            ValidateIssuerSigningKey = true,
        };
        // Чтение токена из httpOnly cookie
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["access_token"];
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// CORS
var myAllowSpecificOrigins = "_myAllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
                      policy  =>
                      {
                          // Разрешаем запросы от Next.js приложения
                          policy.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://localhost:3002", "http://s-lot.ru", "http://www.s-lot.ru")
                                // Разрешаем любые HTTP-методы (GET, POST, OPTIONS и т.д.)
                                .AllowAnyMethod()
                                // Разрешаем любые заголовки в запросе (Content-Type, Authorization и т.д.)
                                .AllowAnyHeader()
                                // Говорим серверу, чтобы он в ответ прислал заголовок Access-Control-Allow-Credentials: true
                                .AllowCredentials();
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
app.UseAuthentication(); 
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