using Lots.Application.Services.DeepSeek;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lots.Application.Extensions;

public static class DeepSeekServiceCollectionExtensions
{
    public static IServiceCollection AddDeepSeekBudgetGuard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DeepSeekBudgetOptions>(configuration.GetSection(DeepSeekBudgetOptions.SectionName));
        services.AddSingleton<IDeepSeekBudgetGuard, DeepSeekBudgetGuard>();
        return services;
    }
}
