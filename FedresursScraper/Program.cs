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
    builder.Services.AddSingleton<IBiddingDataCache, InMemoryBiddingDataCache>();
    builder.Services.AddHostedService<BiddingListParser>();
    builder.Services.AddHostedService<BiddingProcessorService>();

    builder.Services.AddSingleton<IRosreestrQueue, RosreestrQueue>();
    builder.Services.AddHostedService<RosreestrWorker>();
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

builder.Services.AddScoped<IClassificationManager, ClassificationManager>();

builder.Services.AddHostedService<LotClassificationService>();
builder.Services.AddHostedService<LotRecoveryService>();

var rosreestrServiceUrl = Environment.GetEnvironmentVariable("ROSREESTR_SERVICE_URL");

if (string.IsNullOrWhiteSpace(rosreestrServiceUrl))
{
    // Можно выбросить исключение или установить значение по умолчанию для локальной разработки
    //throw new InvalidOperationException("Переменная окружения ROSREESTR_SERVICE_URL не установлена.");
    // или: rosreestrServiceUrl = "http://localhost:8000"; // для локальной отладки
}

// Регистрируем типизированный HttpClient
builder.Services.AddHttpClient<IRosreestrServiceClient, RosreestrServiceClient>(client =>
{
    client.BaseAddress = new Uri(rosreestrServiceUrl);
});

builder.Services.AddHttpClient("FedresursScraper", client =>
{
    // Добавляем User-Agent, который будет использоваться для всех запросов этого клиента
    client.DefaultRequestHeaders.Add(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

    // Здесь можно добавить и другие заголовки, если понадобятся
    // client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
    // client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.8,en-US;q=0.5,en;q=0.3");
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    return new HttpClientHandler
    {
        // 1. Явно указываем, что нужно использовать TLS 1.2. 
        // Это самая частая причина подобных ошибок со старыми сайтами.
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12,

        // 2. В качестве крайней меры, если п.1 не поможет.
        // Этот параметр позволяет использовать более широкий (и менее безопасный) набор шифров,
        // который может требоваться старыми серверами.
        // Раскомментируйте следующую строку, только если явное указание Tls12 не сработало.
        // CipherSuitesPolicy = new CipherSuitePolicy(
        //     new[]
        //     {
        //         TlsCipherSuite.TLS_AES_128_GCM_SHA256,
        //         TlsCipherSuite.TLS_AES_256_GCM_SHA384,
        //         TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
        //         TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        //         TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        //         TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        //         TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        //         TlsCipherSuite.TLS_DHE_RSA_WITH_AES_128_GCM_SHA256,
        //         TlsCipherSuite.TLS_DHE_RSA_WITH_AES_256_GCM_SHA384
        //     })
    };
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
var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policy =>
        {
            // Логика для режима разработки (Debug)
            if (builder.Environment.IsDevelopment())
            {
                policy.SetIsOriginAllowed(origin => true) // Разрешаем любой Origin
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials(); // Разрешаем Credentials (куки, auth-заголовки)
            }
            // Логика для остальных режимов (Production)
            else
            {
                var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");

                if (!string.IsNullOrEmpty(allowedOrigins))
                {
                    var origins = allowedOrigins.Split(',').Select(o => o.Trim()).ToArray();
                    policy.WithOrigins(origins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
            }
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