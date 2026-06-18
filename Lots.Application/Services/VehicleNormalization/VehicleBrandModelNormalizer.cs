using Lots.Application.Services.VehicleFilters;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lots.Application.Services.VehicleNormalization;

public class VehicleBrandModelNormalizer : IVehicleBrandModelNormalizer
{
    private readonly Dictionary<string, string> _brandAliases;
    private readonly Dictionary<string, Dictionary<string, string>> _modelAliasesByBrand;
    private readonly Dictionary<string, List<string>> _modelKeysByBrandLongestFirst;
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
        _modelKeysByBrandLongestFirst = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

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
            _modelKeysByBrandLongestFirst[brand.Canonical] = modelMap.Keys
                .OrderByDescending(key => key.Length)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            return (null, NormalizeModel(model, null).Model, false, false);
        }

        var normalizedBrandInput = NormalizeLookupKey(brand);
        var brandMatched = _brandAliases.TryGetValue(normalizedBrandInput, out var canonicalBrand);

        if (!brandMatched)
        {
            var unmatchedModel = NormalizeModel(model, null);
            return (normalizedBrandInput, unmatchedModel.Model, false, unmatchedModel.Matched);
        }

        var normalizedModel = NormalizeModel(model, canonicalBrand);

        return (canonicalBrand, normalizedModel.Model, true, normalizedModel.Matched);
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

    private (string? Model, bool Matched) NormalizeModel(string? model, string? canonicalBrand)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return (null, false);
        }

        var normalizedModelInput = NormalizeModelLookupKey(model);

        if (!string.IsNullOrWhiteSpace(canonicalBrand)
            && _modelAliasesByBrand.TryGetValue(canonicalBrand, out var modelMap)
            && TryResolveModel(normalizedModelInput, canonicalBrand, modelMap, out var canonicalModel))
        {
            return (canonicalModel, true);
        }

        return (normalizedModelInput, false);
    }

    private bool TryResolveModel(
        string normalizedModelInput,
        string canonicalBrand,
        Dictionary<string, string> modelMap,
        out string canonicalModel)
    {
        if (modelMap.TryGetValue(normalizedModelInput, out canonicalModel!))
        {
            return true;
        }

        if (string.Equals(canonicalBrand, "BMW", StringComparison.OrdinalIgnoreCase)
            && TryResolveBmwXSeriesGluedModel(normalizedModelInput, out var bmwXModel)
            && modelMap.TryGetValue(bmwXModel, out canonicalModel!))
        {
            return true;
        }

        if (!_modelKeysByBrandLongestFirst.TryGetValue(canonicalBrand, out var prefixes))
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!IsModelPrefixMatch(normalizedModelInput, prefix))
            {
                continue;
            }

            canonicalModel = modelMap[prefix];
            return true;
        }

        return false;
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

    private static string NormalizeModelLookupKey(string value)
    {
        return ReplaceCyrillicHomoglyphs(NormalizeLookupKey(value));
    }

    private static string ReplaceCyrillicHomoglyphs(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                'А' => 'A',
                'а' => 'a',
                'В' => 'B',
                'в' => 'b',
                'Е' => 'E',
                'е' => 'e',
                'К' => 'K',
                'к' => 'k',
                'М' => 'M',
                'м' => 'm',
                'Н' => 'H',
                'н' => 'h',
                'О' => 'O',
                'о' => 'o',
                'Р' => 'P',
                'р' => 'p',
                'С' => 'C',
                'с' => 'c',
                'Т' => 'T',
                'т' => 't',
                'У' => 'Y',
                'у' => 'y',
                'Х' => 'X',
                'х' => 'x',
                _ => ch
            });
        }

        return builder.ToString();
    }

    private static bool IsModelPrefixMatch(string input, string prefix)
    {
        if (prefix.Length == 0 || input.Length < prefix.Length)
        {
            return false;
        }

        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (input.Length == prefix.Length)
        {
            return true;
        }

        return input[prefix.Length] is ' ' or '-' or '(';
    }

    private static bool TryResolveBmwXSeriesGluedModel(string input, out string model)
    {
        model = string.Empty;

        if (input.Length < 4 || input[0] != 'X')
        {
            return false;
        }

        var digitEnd = 1;
        while (digitEnd < input.Length && char.IsDigit(input[digitEnd]))
        {
            digitEnd++;
        }

        if (digitEnd == 1)
        {
            return false;
        }

        var suffix = input[digitEnd..];
        if (!suffix.StartsWith("SDRIVE", StringComparison.OrdinalIgnoreCase)
            && !suffix.StartsWith("XDRIVE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        model = "X" + input[1..digitEnd];
        return true;
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
