using FedresursScraper.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using System.Net;
using FedresursScraper.Integrations.Fedresurs.Models;
using FedresursScraper.Integrations.Fedresurs.Clients;
using FedresursScraper.Integrations.Fedresurs.Workers;
using FedresursScraper.Integrations.Fedresurs.Processors;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Lots.Data;

namespace FedresursScraper.Extensions;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddLotsDbContext(this IServiceCollection services, string connectionString)
    {
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        void Configure(DbContextOptionsBuilder options)
        {
            options.UseNpgsql(dataSource);
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }

        // Scoped DbContext для обычных запросов + scoped factory для параллельных
        // (factory не может быть singleton: DbContextOptions от AddDbContext — scoped)
        services.AddDbContext<LotsDbContext>(Configure);
        services.AddDbContextFactory<LotsDbContext>(Configure, ServiceLifetime.Scoped);

        return services;
    }

    public static IServiceCollection AddEnrichmentServices(this IServiceCollection services, IConfiguration configuration)
    {
        // === МЭТС ===
        services.Configure<MetsEnrichmentOptions>(
            configuration.GetSection("MetsEnrichment"));

        // Регистрируем HttpClient и Сервис одной командой
        services.AddHttpClient<IMetsEnrichmentService, MetsEnrichmentService>(client =>
        {
            client.BaseAddress = new Uri("https://m-ets.ru/");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Заголовки
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        });

        // Регистрируем Воркер
        services.AddHostedService<MetsEnrichmentWorker>();


        // === ЦДТ ===
        // Регистрируем HttpClient и Сервис
        services.AddHttpClient<ICdtEnrichmentService, CdtEnrichmentService>(client =>
        {
            client.BaseAddress = new Uri("https://bankrot.cdtrf.ru/");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Заголовки
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        });

        // Регистрируем Воркер
        services.AddHostedService<CdtEnrichmentWorker>();


        // === Альфалот ===
        // HTML через Selenium (InProtect WAF), регистрация scoped + hosted workers
        services.Configure<AlfalotEnrichmentOptions>(
            configuration.GetSection("AlfalotEnrichment"));

        services.AddScoped<IAlfalotCatalogIndexerService, AlfalotCatalogIndexerService>();
        services.AddScoped<IAlfalotEnrichmentService, AlfalotEnrichmentService>();

        services.AddHostedService<AlfalotCatalogIndexerWorker>();
        services.AddHostedService<AlfalotEnrichmentWorker>();


        // === РАД ===
        services.AddHttpClient<RadParserService>(client =>
        {
            client.BaseAddress = new Uri("https://catalog.lot-online.ru/");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Имитация современного Chrome (Windows)
            client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add(HeaderNames.Accept, "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, "gzip, deflate, br");

            // Дополнительные браузерные заголовки для обхода антифрода
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua", "\"Chromium\";v=\"134\", \"Not:A-Brand\";v=\"24\", \"Google Chrome\";v=\"134\"");
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Mobile", "?0");
            client.DefaultRequestHeaders.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        });

        return services;
    }

    /// <summary>
    /// Регистрирует все сервисы, клиенты и настройки для работы с официальным API Федресурса.
    /// </summary>
    public static IServiceCollection AddFedresursApiIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Регистрация настроек (Options)
        services.Configure<FedresursApiOptions>(configuration.GetSection("FedresursApi"));
        services.Configure<FedresursWorkerOptions>(configuration.GetSection("FedresursWorkers"));

        // Регистрация HTTP-клиента
        services.AddHttpClient<IFedresursApiClient, FedresursApiClient>(client =>
        {
            var baseUrl = configuration["FedresursApi:BaseUrl"]
                          ?? "https://bank-publications-demo.fedresurs.ru/";
            client.BaseAddress = new Uri(baseUrl);
        });

        // Регистрация фоновых задач (Workers)
        services.AddHostedService<FedresursAggregatorService>();
        services.AddHostedService<FedresursMessageProcessorService>();

        return services;
    }

    /// <summary>
    /// Регистрирует клиенты Amazon S3 и типизированные сервисы хранилища для разных бакетов.
    /// </summary>
    public static IServiceCollection AddFileStorageServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Регистрируем единый потокобезопасный клиент IAmazonS3 (Singleton)
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var accessKey = configuration["S3:AccessKey"] ?? throw new ArgumentNullException("S3:AccessKey");
            var secretKey = configuration["S3:SecretKey"] ?? throw new ArgumentNullException("S3:SecretKey");
            var serviceUrl = configuration["S3:ServiceUrl"] ?? throw new ArgumentNullException("S3:ServiceUrl");
            var useHttp = serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            var s3Config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
                UseHttp = useHttp
            };

            return new AmazonS3Client(accessKey, secretKey, s3Config);
        });

        // Регистрируем типизированные сервисы
        services.AddTransient<IUserAdsFileStorageService, UserAdsFileStorageService>();
        services.AddTransient<ILotsFileStorageService, LotsFileStorageService>();

        return services;
    }
}
