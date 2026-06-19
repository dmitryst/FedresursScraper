namespace FedresursScraper.Services;

/// <summary>
/// Разделение описания лота и порядка ознакомления (как при парсинге Федресурса).
/// </summary>
public static class LotDescriptionTextHelper
{
    private const string Marker1 = "Порядок ознакомления с имуществом должника:";
    private const string Marker2 = "С имуществом можно ознакомиться";

    public static (string Description, string ViewingProcedure) SplitDescriptionAndViewing(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, string.Empty);

        var description = raw.Trim();
        var viewingProcedure = string.Empty;

        var idx1 = raw.IndexOf(Marker1, StringComparison.OrdinalIgnoreCase);
        if (idx1 >= 0)
        {
            description = raw[..idx1].Trim();
            var afterStart = idx1 + Marker1.Length;
            if (afterStart <= raw.Length)
                viewingProcedure = raw[afterStart..].Trim();
            viewingProcedure = viewingProcedure.TrimStart(':', '.', ',', ' ');
        }
        else
        {
            var idx2 = raw.IndexOf(Marker2, StringComparison.OrdinalIgnoreCase);
            if (idx2 >= 0)
            {
                description = raw[..idx2].Trim();
                viewingProcedure = raw[idx2..].Trim();
            }
        }

        return (description, viewingProcedure);
    }

    public static string MergeViewingProcedureParts(params string?[] parts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var trimmed = part.Trim();
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }

        return string.Join("\n\n", result);
    }
}
