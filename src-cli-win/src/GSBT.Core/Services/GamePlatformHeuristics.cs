namespace GSBT.Core.Services;

/// <summary>Store/platform hints from registry uninstall rows and install folders (Python <c>_detect_platform_from_registry_entry</c> parity).</summary>
public static class GamePlatformHeuristics
{
    public const string OtherLabel = "Other";

    private static readonly string[] KnownStorePlatforms =
    [
        "Steam",
        "GOG",
        "Epic",
        "Ubisoft",
        "EA",
        "Battle.net",
        "Xbox",
        "Rockstar",
        "Bethesda",
    ];
    public static string DetectFromRegistry(
        string? displayName,
        string? publisher,
        string? installLocation,
        string? uninstallString = null)
    {
        var pub = (publisher ?? string.Empty).ToLowerInvariant();
        var installLower = (installLocation ?? string.Empty).ToLowerInvariant();
        var uninstallLower = (uninstallString ?? string.Empty).ToLowerInvariant();

        if (pub.Contains("gog", StringComparison.Ordinal)
            || pub.Contains("gog.com", StringComparison.Ordinal)
            || installLower.Contains("gog", StringComparison.Ordinal)
            || installLower.Contains("galaxy", StringComparison.Ordinal)
            || uninstallLower.Contains("goggame", StringComparison.Ordinal)
            || uninstallLower.Contains("gog galaxy", StringComparison.Ordinal))
        {
            return "GOG";
        }

        if (pub.Contains("epic", StringComparison.Ordinal) || installLower.Contains("epic", StringComparison.Ordinal))
        {
            return "Epic";
        }

        if (pub.Contains("ubisoft", StringComparison.Ordinal) || installLower.Contains("uplay", StringComparison.Ordinal))
        {
            return "Ubisoft";
        }

        if (pub.Contains("valve", StringComparison.Ordinal)
            || installLower.Contains("steamapps", StringComparison.Ordinal)
            || installLower.Contains("steam", StringComparison.Ordinal))
        {
            return "Steam";
        }

        if (pub.Contains("electronic arts", StringComparison.Ordinal) || installLower.Contains("origin", StringComparison.Ordinal))
        {
            return "EA";
        }

        if (pub.Contains("blizzard", StringComparison.Ordinal) || installLower.Contains("battle.net", StringComparison.Ordinal))
        {
            return "Battle.net";
        }

        if (pub.Contains("rockstar", StringComparison.Ordinal))
        {
            return "Rockstar";
        }

        if (pub.Contains("bethesda", StringComparison.Ordinal))
        {
            return "Bethesda";
        }

        if (pub.Contains("microsoft", StringComparison.Ordinal) && installLower.Contains("windowsapps", StringComparison.Ordinal))
        {
            return "Xbox";
        }

        if (pub.Contains("xbox", StringComparison.Ordinal))
        {
            return "Xbox";
        }

        return OtherLabel;
    }

    public static string? InferFromInstallPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        var platform = DetectFromRegistry(null, null, installPath);
        return IsKnownStorePlatform(platform) ? platform : null;
    }

    public static string FormatForDisplay(string? platform)
    {
        if (IsKnownStorePlatform(platform)
            || string.Equals(platform, "Custom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, OtherLabel, StringComparison.OrdinalIgnoreCase))
        {
            return platform!;
        }

        return OtherLabel;
    }

    public static bool IsStorePlatform(string? platform) =>
        !string.IsNullOrWhiteSpace(platform)
        && (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "GOG", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "Epic", StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownStorePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        foreach (var store in KnownStorePlatforms)
        {
            if (string.Equals(platform, store, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUnknownOrEmpty(string? platform) =>
        string.IsNullOrWhiteSpace(platform)
        || string.Equals(platform, "Unknown", StringComparison.OrdinalIgnoreCase)
        || string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase);
}
