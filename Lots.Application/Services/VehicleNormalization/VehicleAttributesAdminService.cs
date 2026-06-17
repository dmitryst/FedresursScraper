using Lots.Application.Services.VehicleFilters;
using Lots.Data;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lots.Application.Services.VehicleNormalization;

public class VehicleAttributesAdminService : IVehicleAttributesAdminService
{
    private const string VehicleCategory = "Легковой автомобиль";

    private readonly LotsDbContext _dbContext;
    private readonly IVehicleAttributesNormalizationService _normalizationService;
    private readonly IVehicleFilterOptionsCache _filterOptionsCache;

    public VehicleAttributesAdminService(
        LotsDbContext dbContext,
        IVehicleAttributesNormalizationService normalizationService,
        IVehicleFilterOptionsCache filterOptionsCache)
    {
        _dbContext = dbContext;
        _normalizationService = normalizationService;
        _filterOptionsCache = filterOptionsCache;
    }

    public async Task<(IReadOnlyList<UnmatchedVehicleLotDto> Items, int TotalCount)> GetUnmatchedLotsAsync(
        int page,
        int pageSize,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = BuildUnmatchedQuery(activeOnly);
        var totalCount = await query.CountAsync(cancellationToken);

        var lots = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.PublicId,
                l.LotNumber,
                l.Title,
                l.Slug,
                l.StartPrice,
                TradeNumber = l.Bidding.TradeNumber,
                Platform = l.Bidding.Platform,
                l.Attributes
            })
            .ToListAsync(cancellationToken);

        var items = lots.Select(l => MapToDto(
            l.Id,
            l.PublicId,
            l.LotNumber,
            l.Title,
            l.Slug,
            l.StartPrice,
            l.TradeNumber,
            l.Platform,
            l.Attributes)).ToList();
        return (items, totalCount);
    }

    public Task<int> GetUnmatchedLotsCountAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        return BuildUnmatchedQuery(activeOnly).CountAsync(cancellationToken);
    }

    public async Task<UnmatchedVehicleLotDto?> UpdateLotVehicleAttributesAsync(
        int publicId,
        UpdateLotVehicleAttributesRequest request,
        CancellationToken cancellationToken = default)
    {
        var lot = await _dbContext.Lots
            .Include(l => l.Bidding)
            .Where(l => l.PublicId == publicId)
            .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
            .FirstOrDefaultAsync(cancellationToken);

        if (lot == null)
        {
            return null;
        }

        var attributes = lot.Attributes != null
            ? new Dictionary<string, string>(lot.Attributes)
            : new Dictionary<string, string>();

        if (request.RemoveBrand)
        {
            RemoveAttributeKeys(attributes, "brand", "brand_raw", "model", "model_raw", "_model_matched");
        }
        else if (!string.IsNullOrWhiteSpace(request.Brand))
        {
            attributes["brand"] = request.Brand.Trim();
        }

        if (request.RemoveModel)
        {
            RemoveAttributeKeys(attributes, "model", "model_raw", "_model_matched");
        }
        else if (!string.IsNullOrWhiteSpace(request.Model))
        {
            attributes["model"] = request.Model.Trim();
        }

        if (!attributes.ContainsKey("brand"))
        {
            RemoveAttributeKeys(attributes, "model", "model_raw", "_brand_matched", "_model_matched");
        }

        if (attributes.ContainsKey("brand") || attributes.ContainsKey("model"))
        {
            _normalizationService.NormalizeAttributes(attributes);
        }
        else
        {
            attributes.Remove("_brand_normalized");
            attributes.Remove("_brand_matched");
            attributes.Remove("_model_matched");
        }

        lot.Attributes = attributes.Count > 0 ? attributes : null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _filterOptionsCache.RefreshAsync(cancellationToken);

        return MapToDto(
            lot.Id,
            lot.PublicId,
            lot.LotNumber,
            lot.Title,
            lot.Slug,
            lot.StartPrice,
            lot.Bidding.TradeNumber,
            lot.Bidding.Platform,
            lot.Attributes);
    }

    private static UnmatchedVehicleLotDto MapToDto(
        Guid id,
        int publicId,
        string? lotNumber,
        string? title,
        string? slug,
        decimal? startPrice,
        string? tradeNumber,
        string? platform,
        Dictionary<string, string>? attributes)
    {
        var resolvedSlug = !string.IsNullOrWhiteSpace(slug)
            ? slug
            : (!string.IsNullOrWhiteSpace(title) ? SlugHelper.GenerateSlug(title) : "lot");

        return new UnmatchedVehicleLotDto(
            id,
            publicId,
            lotNumber,
            title,
            $"/lot/{resolvedSlug}-{publicId}",
            tradeNumber,
            platform,
            startPrice,
            GetAttribute(attributes, "brand"),
            GetAttribute(attributes, "model"),
            GetAttribute(attributes, "brand_raw"),
            GetAttribute(attributes, "model_raw"),
            ParseBoolAttribute(attributes, "_brand_matched"),
            ParseBoolAttribute(attributes, "_model_matched"));
    }

    private IQueryable<Lot> BuildUnmatchedQuery(bool activeOnly)
    {
        var query = _dbContext.Lots
            .AsNoTracking()
            .Where(l => l.Categories.Any(c => c.Name == VehicleCategory))
            .Where(l => l.Attributes != null && EF.Functions.JsonExists(l.Attributes, "brand"))
            .Where(l =>
                !EF.Functions.JsonExists(l.Attributes!, "_brand_matched")
                || LotsDbContext.JsonbExtractPathText(l.Attributes!, "_brand_matched") == "false"
                || (
                    EF.Functions.JsonExists(l.Attributes!, "model")
                    && (
                        !EF.Functions.JsonExists(l.Attributes!, "_model_matched")
                        || LotsDbContext.JsonbExtractPathText(l.Attributes!, "_model_matched") == "false")));

        if (activeOnly)
        {
            query = query.Where(Lot.IsActiveExpression);
        }

        return query;
    }

    private static string? GetAttribute(Dictionary<string, string>? attributes, string key)
    {
        if (attributes == null || !attributes.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static bool? ParseBoolAttribute(Dictionary<string, string>? attributes, string key)
    {
        var value = GetAttribute(attributes, key);
        if (value == null)
        {
            return null;
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveAttributeKeys(Dictionary<string, string> attributes, params string[] keys)
    {
        foreach (var key in keys)
        {
            attributes.Remove(key);
        }
    }
}
