using Lots.Application.Services.VehicleFilters;
using Lots.Application.Services.VehicleNormalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lots.Application.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Справочник марок/моделей и нормализация атрибутов brand/model.
    /// </summary>
    public static IServiceCollection AddVehicleNormalization(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerBackfillWorker = false)
    {
        services.Configure<VehicleCatalogSettings>(configuration.GetSection("VehicleCatalog"));
        services.Configure<VehicleNormalizationSettings>(configuration.GetSection("VehicleNormalization"));
        services.AddSingleton<IVehicleBrandModelNormalizer, VehicleBrandModelNormalizer>();
        services.AddSingleton<IVehicleAttributesNormalizationService, VehicleAttributesNormalizationService>();

        if (registerBackfillWorker &&
            configuration.GetValue("VehicleNormalization:BackfillEnabled", true))
        {
            services.AddHostedService<VehicleAttributesNormalizationWorker>();
        }

        return services;
    }

    /// <summary>
    /// Admin API для неразобранных марок/моделей. Только WebApi (зависит от IVehicleFilterOptionsCache).
    /// </summary>
    public static IServiceCollection AddVehicleAttributesAdmin(this IServiceCollection services)
    {
        services.AddScoped<IVehicleUnmatchedAttributesService, VehicleUnmatchedAttributesService>();
        services.AddScoped<IVehicleAttributesAdminService, VehicleAttributesAdminService>();

        return services;
    }

    /// <summary>
    /// In-memory кэш марок/моделей для фильтра «Легковой автомобиль» и фоновое обновление по интервалу.
    /// </summary>
    public static IServiceCollection AddVehicleFilterOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<VehicleFilterOptionsCacheSettings>(
            configuration.GetSection("VehicleFilterOptions"));
        services.AddSingleton<IVehicleFilterOptionsCache, VehicleFilterOptionsCache>();
        services.AddHostedService<VehicleFilterOptionsRefreshWorker>();

        return services;
    }
}
