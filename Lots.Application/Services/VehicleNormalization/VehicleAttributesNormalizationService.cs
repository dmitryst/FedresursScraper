namespace Lots.Application.Services.VehicleNormalization;

public class VehicleAttributesNormalizationService : IVehicleAttributesNormalizationService
{
    private readonly IVehicleBrandModelNormalizer _normalizer;

    public VehicleAttributesNormalizationService(IVehicleBrandModelNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public bool NormalizeAttributes(Dictionary<string, string> attributes)
    {
        attributes.TryGetValue("brand", out var rawBrand);
        attributes.TryGetValue("model", out var rawModel);

        var (brand, model, brandMatched, modelMatched) = _normalizer.Normalize(rawBrand, rawModel);
        var changed = false;

        if (!string.IsNullOrWhiteSpace(brand))
        {
            if (ShouldStoreRaw(rawBrand, brand))
            {
                attributes["brand_raw"] = rawBrand!.Trim();
                changed = true;
            }

            if (!string.Equals(attributes.GetValueOrDefault("brand"), brand, StringComparison.Ordinal))
            {
                attributes["brand"] = brand;
                changed = true;
            }

            changed |= SetFlag(attributes, "_brand_matched", brandMatched);
        }
        else
        {
            attributes.Remove("_brand_matched");
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            if (ShouldStoreRaw(rawModel, model))
            {
                attributes["model_raw"] = rawModel!.Trim();
                changed = true;
            }

            if (!string.Equals(attributes.GetValueOrDefault("model"), model, StringComparison.Ordinal))
            {
                attributes["model"] = model;
                changed = true;
            }

            var modelResolved = brandMatched && modelMatched;
            changed |= SetFlag(attributes, "_model_matched", modelResolved);
        }
        else
        {
            attributes.Remove("_model_matched");
        }

        changed |= SetFlag(attributes, "_brand_normalized", true);

        return changed;
    }

    private static bool SetFlag(Dictionary<string, string> attributes, string key, bool value)
    {
        var stringValue = value ? "true" : "false";

        if (attributes.TryGetValue(key, out var existing) && existing == stringValue)
        {
            return false;
        }

        attributes[key] = stringValue;
        return true;
    }

    private static bool ShouldStoreRaw(string? raw, string? normalized)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return !string.Equals(
            raw.Trim(),
            normalized,
            StringComparison.OrdinalIgnoreCase);
    }
}