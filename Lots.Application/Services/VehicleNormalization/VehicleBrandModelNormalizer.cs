using Lots.Application.Services.VehicleFilters;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lots.Application.Services.VehicleNormalization;

public class VehicleBrandModelNormalizer : IVehicleBrandModelNormalizer
{
    private readonly Dictionary<string, string> _brandAliases;
    private readonly Dictionary<string, Dictionary<string, string>> _modelAliasesByBrand;
    private readonly List<VehicleBrandEntry> _catalogBrands;
    private readonly ILogger<VehicleBrandModelNormalizer> _logger;

    public VehicleBrandModelNormalizer(
        IOptions<VehicleCatalogSettings> settings,
        ILogger<VehicleBrandModelNormalizer> logger)
    {
        _logger = logger;
        var catalog = LoadCatalog(settings.Value);
        _catalogBrands = catalog.Brands;

        _brandAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _modelAliasesByBrand = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var brand in catalog.Brands)
        {
            RegisterBrandAlias(brand.Canonical, brand.Canonical);

            foreach (var alias in brand.Aliases)
            {
                RegisterBrandAlias(alias, brand.Canonical);
            }

            var modelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var model in brand.Models)
            {
                RegisterModelAlias(modelMap, model.Canonical, model.Canonical);

                foreach (var alias in model.Aliases)
                {
                    RegisterModelAlias(modelMap, alias, model.Canonical);
                }
            }

            _modelAliasesByBrand[brand.Canonical] = modelMap;
        }

        _logger.LogInformation(
            "Справочник марок/моделей загружен: {BrandCount} марок, {AliasCount} алиасов марок.",
            catalog.Brands.Count,
            _brandAliases.Count);
    }

    public (string? Brand, string? Model, bool BrandMatched, bool ModelMatched) Normalize(
        string? brand,
        string? model)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            return (null, NormalizeModel(model, null), false, false);
        }

        var normalizedBrandInput = NormalizeLookupKey(brand);
        var brandMatched = _brandAliases.TryGetValue(normalizedBrandInput, out var canonicalBrand);

        if (!brandMatched)
        {
            return (normalizedBrandInput, NormalizeModel(model, null), false, false);
        }

        var normalizedModel = NormalizeModel(model, canonicalBrand);
        var modelMatched = !string.IsNullOrWhiteSpace(model)
            && _modelAliasesByBrand.TryGetValue(canonicalBrand!, out var modelMap)
            && modelMap.ContainsKey(NormalizeLookupKey(model));

        return (canonicalBrand, normalizedModel, true, modelMatched);
    }

    public VehicleFilterOptions GetCatalogOptions()
    {
        var modelsByBrand = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var brand in _catalogBrands)
        {
            modelsByBrand[brand.Canonical] = brand.Models
                .Select(m => m.Canonical)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new VehicleFilterOptions
        {
            Brands = _catalogBrands
                .Select(b => b.Canonical)
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ModelsByBrand = modelsByBrand,
            CachedAtUtc = DateTime.UtcNow
        };
    }

    private string? NormalizeModel(string? model, string? canonicalBrand)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var normalizedModelInput = NormalizeLookupKey(model);

        if (!string.IsNullOrWhiteSpace(canonicalBrand)
            && _modelAliasesByBrand.TryGetValue(canonicalBrand, out var modelMap)
            && modelMap.TryGetValue(normalizedModelInput, out var canonicalModel))
        {
            return canonicalModel;
        }

        return normalizedModelInput;
    }

    private void RegisterBrandAlias(string alias, string canonical)
    {
        var key = NormalizeLookupKey(alias);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_brandAliases.TryGetValue(key, out var existing) &&
            !string.Equals(existing, canonical, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Конфликт алиаса марки '{Alias}': '{Existing}' и '{New}'.",
                key,
                existing,
                canonical);
            return;
        }

        _brandAliases[key] = canonical;
    }

    private static void RegisterModelAlias(
        Dictionary<string, string> modelMap,
        string alias,
        string canonical)
    {
        var key = NormalizeLookupKey(alias);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        modelMap.TryAdd(key, canonical);
    }

    private static string NormalizeLookupKey(string value)
    {
        var trimmed = value.Trim();
        return string.Join(' ', trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static VehicleCatalog LoadCatalog(VehicleCatalogSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CatalogPath) && File.Exists(settings.CatalogPath))
        {
            return DeserializeCatalog(File.ReadAllText(settings.CatalogPath));
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("vehicle-catalog.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new FileNotFoundException(
                "Встроенный справочник марок/моделей не найден. Укажите VehicleCatalog:CatalogPath.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Не удалось прочитать ресурс {resourceName}.");

        using var reader = new StreamReader(stream);
        return DeserializeCatalog(reader.ReadToEnd());
    }

    private static VehicleCatalog DeserializeCatalog(string json)
    {
        return JsonSerializer.Deserialize<VehicleCatalog>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Не удалось десериализовать справочник марок/моделей.");
    }
}
