using Lots.Data;
using Microsoft.EntityFrameworkCore;
using FedresursScraper.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using FedresursScraper.Extensions;
using FedresursScraper.TradeStatuses;
using Microsoft.EntityFrameworkCore.Diagnostics;
using FedresursScraper.Services.LotAlerts;
using FedresursScraper.Services.Email;
using System.Net;

// Используем WebApplicationBuilder для создания веб-приложения
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Регистрация сервисов в DI контейнере
builder.Services.AddControllers(); // Добавляем поддержку API-контроллеров

var connectionString = configuration.GetConnectionString("Postgres");

// Регистрация DbContext
builder.Services.AddDbContext<LotsDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});


// Регистрация фабрики для создания ChromeDriver
builder.Services.AddSingleton<IWebDriverFactory, WebDriverFactory>();

// Регистрация парсеров
builder.Services.AddTransient<IBiddingScraper, BiddingScraper>();
builder.Services.AddTransient<ILotsScraperFromBankruptMessagePage, LotsScraperFromBankruptMessagePage>();
builder.Services.AddTransient<ILotsScraperFromLotsPage, LotsScraperFromLotsPage>();
builder.Services.AddTransient<ITradeCardLotsStatusScraper, TradeCardLotsStatusScraper>();

// Регистрация других сервисов
builder.Services.AddTransient<ICadastralNumberExtractor, CadastralNumberExtractor>();
builder.Services.AddTransient<IRosreestrService, RosreestrService>();
builder.Services.AddScoped<ILotCopyService, LotCopyService>();

// Регистрация фоновых сервисов
bool parsersEnabled = configuration.GetValue<bool>("BackgroundParsers:Enabled");

if (parsersEnabled)
{
    builder.Services.AddSingleton<IBiddingDataCache, InMemoryBiddingDataCache>();
    //builder.Services.AddHostedService<BiddingListParser>();
    builder.Services.AddHostedService<BiddingListParserSeleniumImpl>();
    //builder.Services.AddHostedService<BiddingProcessorService>();

    builder.Services.AddHostedService<RosreestrWorker>();
}

builder.Services.AddSingleton<IRosreestrQueue, RosreestrQueue>();

builder.Services.AddSingleton<ILotClassifier>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<LotClassifier>>();

    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    string apiKey = configuration["DeepSeek:ApiKey"] ??
        throw new InvalidOperationException("API ключ для DeepSeek не найден в конфигурации (DeepSeek:ApiKey).");
    string apiUrl = configuration["DeepSeek:ApiUrl"] ??
        throw new InvalidOperationException("API URL для DeepSeek не найден в конфигурации (DeepSeek:ApiUrl).");

    return new LotClassifier(logger, configuration, apiKey, apiUrl);
});

builder.Services.AddSingleton<IClassificationQueue, ClassificationQueue>();

builder.Services.AddScoped<IClassificationManager, ClassificationManager>();

builder.Services.AddHostedService<LotClassificationService>();
builder.Services.AddHostedService<LotRecoveryService>();
//builder.Services.AddHostedService<TradeStatusesUpdateBackgroundService>();
builder.Services.AddHostedService<LotAlertMatchingWorker>();
builder.Services.AddHostedService<LotAlertDeliveryWorker>();

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

builder.Services.AddScoped<ILotEvaluationService, LotEvaluationService>();

var rosreestrServiceUrl = Environment.GetEnvironmentVariable("ROSREESTR_SERVICE_URL");

builder.Services.AddSingleton<IFileStorageService, S3FileStorageService>();

// Регистрируем сервисы дообогащения торгов (МЭТС, ЦДТ)
builder.Services.AddEnrichmentServices(configuration);

if (string.IsNullOrWhiteSpace(rosreestrServiceUrl))
{
    // Можно выбросить исключение или установить значение по умолчанию для локальной разработки
    //throw new InvalidOperationException("Переменная окружения ROSREESTR_SERVICE_URL не установлена.");
    rosreestrServiceUrl = "http://localhost:8000"; // для локальной отладки
}

// Регистрируем типизированный HttpClient
builder.Services.AddHttpClient<IRosreestrServiceClient, RosreestrServiceClient>(client =>
{
    client.BaseAddress = new Uri(rosreestrServiceUrl);
});

builder.Services.AddHttpClient("FedresursScraper", client =>
{
    // Максимально имитируем реальный браузер, чтобы обойти WAF
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
    client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
    client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
    client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
    client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler
    {
        SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
        // Критично важно для Федресурса (ASP.NET сессии и куки WAF)
        // UseCookies = true,
        // CookieContainer = new CookieContainer(),
        // // WAF часто требует поддержки сжатия
        // AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        // // Заставляет HttpClient отправлять креды прокси сразу, не дожидаясь ответа 407
        // PreAuthenticate = true,
        // Ограничиваем время жизни соединения, чтобы ротация IP прокси работала корректно
        //PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };

    var useProxy = false;
    if (useProxy)
    {
        var proxyHost = configuration["ProxySettings:Host"];
        var proxyPortString = configuration["ProxySettings:Port"];

        if (!string.IsNullOrWhiteSpace(proxyHost) && int.TryParse(proxyPortString, out int proxyPort))
        {
            // Явное конструирование через Uri надежнее для некоторых провайдеров
            var proxy = new WebProxy($"http://{proxyHost}:{proxyPort}")
            {
                BypassProxyOnLocal = false
            };

            var proxyUser = configuration["ProxySettings:Username"];
            var proxyPass = configuration["ProxySettings:Password"];

            if (!string.IsNullOrWhiteSpace(proxyUser) && !string.IsNullOrWhiteSpace(proxyPass))
            {
                proxy.Credentials = new NetworkCredential(proxyUser, proxyPass);
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
    }

    return handler;
});

builder.Services.AddHttpClient<IIndexNowService, IndexNowService>();
builder.Services.AddHttpClient<ICdtTradeStatusScraper, CdtTradeStatusScraper>();

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

// --- Интеграция с официальным API Федресурса (Заморожено до монетизации) ---
// Чтобы включить обратно, просто раскомментируй строку ниже:
// builder.Services.AddFedresursApiIntegration(configuration);

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