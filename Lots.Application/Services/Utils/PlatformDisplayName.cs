namespace FedresursScraper.Services.Utils;

public static class PlatformDisplayName
{
    private const string MetsFullName = "Межрегиональная Электронная Торговая Система";
    private const string MetsShortName = "МЭТС";

    public static string GetDisplayName(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return platform ?? string.Empty;
        }

        if (platform.Contains(MetsFullName, StringComparison.OrdinalIgnoreCase))
        {
            return MetsShortName;
        }

        return platform;
    }
}
