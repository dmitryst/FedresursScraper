using Lots.Application.Services.DebtScoring;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FedresursScraper.Services.DebtScoring;

namespace FedresursScraper.Extensions;

public static class DebtScoringServiceCollectionExtensions
{
    public static IServiceCollection AddDebtScoringServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DebtScoringOptions>(configuration.GetSection(DebtScoringOptions.SectionName));
        services.AddDataProtection();

        services.AddSingleton<ICourtActEntityExtractor, CourtActEntityExtractor>();
        services.AddSingleton<DocxDocumentTextExtractor>();
        services.AddScoped<IDebtLotDiscoveryService, DebtLotDiscoveryService>();
        services.AddScoped<IDebtDocumentProcessingService, DebtDocumentProcessingService>();
        services.AddSingleton<IPersonalDataProtector, PersonalDataProtector>();

        services.AddHttpClient("DebtScoring", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        });

        var ocrServiceUrl = configuration["DebtScoring:OcrServiceUrl"]
            ?? Environment.GetEnvironmentVariable("OCR_SERVICE_URL");

        if (!string.IsNullOrWhiteSpace(ocrServiceUrl))
        {
            services.AddHttpClient<IOcrServiceClient, OcrServiceClient>(client =>
            {
                client.BaseAddress = new Uri(ocrServiceUrl);
                client.Timeout = TimeSpan.FromMinutes(5);
            });
            services.AddSingleton<OcrDocumentTextExtractor>();
        }

        services.AddSingleton<IDocumentTextExtractor>(sp =>
        {
            var extractors = new List<IDocumentTextExtractor>
            {
                sp.GetRequiredService<DocxDocumentTextExtractor>(),
            };

            var ocrExtractor = sp.GetService<OcrDocumentTextExtractor>();
            if (ocrExtractor != null)
            {
                extractors.Add(ocrExtractor);
            }

            return new CompositeDocumentTextExtractor(extractors);
        });

        services.AddHostedService<DebtScoringWorker>();

        return services;
    }
}
