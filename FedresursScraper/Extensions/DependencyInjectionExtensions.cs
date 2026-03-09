using FedresursScraper.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using System.Net;

namespace FedresursScraper.Extensions
{
    public static class DependencyInjectionExtensions
    {
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
    }
}
