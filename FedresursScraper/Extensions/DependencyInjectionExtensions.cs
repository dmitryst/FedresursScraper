using FedresursScraper.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

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

            return services;
        }
    }
}
