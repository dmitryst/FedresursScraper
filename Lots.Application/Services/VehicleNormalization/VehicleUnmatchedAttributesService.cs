using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lots.Application.Services.VehicleNormalization;

public class VehicleUnmatchedAttributesService : IVehicleUnmatchedAttributesService
{
    private const string VehicleCategory = "Легковой автомобиль";
    private const int ResetBatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;

    public VehicleUnmatchedAttributesService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<UnmatchedBrandEntry>> GetUnmatchedBrandsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var brands = await dbContext.Lots
            .AsNoTracking()
            .Where(Lot.IsActiveExpression)
            .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
            .Where(l => l.Attributes != null
                && EF.Functions.JsonExists(l.Attributes, "brand")
                && EF.Functions.JsonExists(l.Attributes, "_brand_matched")
                && LotsDbContext.JsonbExtractPathText(l.Attributes, "_brand_matched") == "false")
            .Select(l => LotsDbContext.JsonbExtractPathText(l.Attributes!, "brand"))
            .ToListAsync(cancellationToken);

        return brands
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .GroupBy(b => b.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new UnmatchedBrandEntry(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<UnmatchedModelEntry>> GetUnmatchedModelsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

        var rows = await dbContext.Lots
            .AsNoTracking()
            .Where(Lot.IsActiveExpression)
            .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
            .Where(l => l.Attributes != null
                && EF.Functions.JsonExists(l.Attributes, "brand")
                && EF.Functions.JsonExists(l.Attributes, "model")
                && EF.Functions.JsonExists(l.Attributes, "_brand_matched")
                && EF.Functions.JsonExists(l.Attributes, "_model_matched")
                && LotsDbContext.JsonbExtractPathText(l.Attributes, "_brand_matched") == "true"
                && LotsDbContext.JsonbExtractPathText(l.Attributes, "_model_matched") == "false")
            .Select(l => new
            {
                Brand = LotsDbContext.JsonbExtractPathText(l.Attributes!, "brand"),
                Model = LotsDbContext.JsonbExtractPathText(l.Attributes!, "model")
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Brand) && !string.IsNullOrWhiteSpace(x.Model))
            .GroupBy(
                x => $"{x.Brand.Trim()}|{x.Model.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var parts = g.First();
                return new UnmatchedModelEntry(parts.Brand.Trim(), parts.Model.Trim(), g.Count());
            })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToList();
    }

    public async Task<int> ResetNormalizationFlagsAsync(CancellationToken cancellationToken = default)
    {
        var totalReset = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LotsDbContext>();

            var lots = await dbContext.Lots
                .Where(Lot.IsActiveExpression)
                .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
                .Where(l => l.Attributes != null && EF.Functions.JsonExists(l.Attributes, "brand"))
                .Where(l => EF.Functions.JsonExists(l.Attributes!, "_brand_normalized")
                    || EF.Functions.JsonExists(l.Attributes!, "_brand_matched")
                    || EF.Functions.JsonExists(l.Attributes!, "_model_matched"))
                .OrderBy(l => l.CreatedAt)
                .Take(ResetBatchSize)
                .ToListAsync(cancellationToken);

            if (lots.Count == 0)
            {
                break;
            }

            foreach (var lot in lots)
            {
                var attributes = new Dictionary<string, string>(lot.Attributes!);
                attributes.Remove("_brand_normalized");
                attributes.Remove("_brand_matched");
                attributes.Remove("_model_matched");
                lot.Attributes = attributes;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            totalReset += lots.Count;

            if (lots.Count < ResetBatchSize)
            {
                break;
            }
        }

        return totalReset;
    }
}
