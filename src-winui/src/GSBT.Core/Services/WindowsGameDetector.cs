using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GSBT.Core.Common;
using GSBT.Core.Models;
using Microsoft.Win32;

namespace GSBT.Core.Services;

/// <summary>
/// Windows-native game detection aligned with the Python <c>GameDetector</c>: Steam app manifests,
/// optional GOG/Epic hints from registry uninstall keys, then supplemental registry heuristics.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameDetector : IGameDetector
{
    /// <summary>Matches Python <c>_NON_GAME_TITLE_OR_PUBLISHER_RE</c> (abbreviated high-signal prefixes).</summary>
    private static readonly Regex NonGameTitleOrPublisher = new(
        @"(?ix)microsoft\s+visual\s*c\+\+|vc_?redist|\.net(\s+framework|\s+sdk|\s+runtime)?|"
        + @"windows\s*sdk|directx|redistributable|\bruntime\b|^\s*update\s+for\b|\bkb\d{5,7}\b|"
        + @"\bjava\b|python\s*\d|node\.js|visual\s+studio|nvidia|geforce|amd\s*software|"
        + @"adobe\s|acrobat|\bchrome\b|firefox|microsoft\s*edge|\bdiscord\b|spotify|docker|jetbrains",
        RegexOptions.Compiled);

    private static readonly Regex VdfPairRegex = new("\"([^\"]+)\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

    private HashSet<string>? _steamCommonRootsCache;
    private List<Dictionary<string, string>>? _registryGamesCache;

    public Task<IReadOnlyList<GameRecord>> DetectAllGamesAsync(CancellationToken cancellationToken = default)
    {
        var steam = DetectSteamGames();
        cancellationToken.ThrowIfCancellationRequested();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GameRecord>();

        foreach (var g in steam)
        {
            if (string.IsNullOrWhiteSpace(g.InstallPath))
            {
                continue;
            }

            var key = Path.GetFullPath(g.InstallPath.TrimEnd('\\', '/')).ToLowerInvariant();
            if (seen.Add(key))
            {
                result.Add(g);
            }
        }

        var registry = DetectRegistryGames();
        foreach (var row in registry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = row.GetValueOrDefault("install_path");
            var platform = row.GetValueOrDefault("platform") ?? "PC";
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var nk = Path.GetFullPath(path.TrimEnd('\\', '/')).ToLowerInvariant();
            if (!seen.Add(nk))
            {
                continue;
            }

            // Python skips Steam/GOG/Epic from generic registry pass when deduping — handled by seen_paths.
            var rid = row.GetValueOrDefault("app_id");
            result.Add(new GameRecord(
                row.GetValueOrDefault("name") ?? "Unknown",
                string.IsNullOrWhiteSpace(rid) ? null : rid,
                path,
                platform,
                GameDisplayName.CleanDisplayName(row.GetValueOrDefault("name") ?? "Unknown")));
        }

        return Task.FromResult<IReadOnlyList<GameRecord>>(result);
    }

    private static Dictionary<string, string> ParseVdfFlat(string content)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in VdfPairRegex.Matches(content))
        {
            data[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        return data;
    }

    private static string? TryGetSteamInstallPath()
    {
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(keyPath);
                var v = k?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(v) && Directory.Exists(v))
                {
                    return v;
                }
            }
            catch
            {
                // continue
            }
        }

        return null;
    }

    private HashSet<string> GetSteamLibraryCommonRoots()
    {
        if (_steamCommonRootsCache is not null)
        {
            return _steamCommonRootsCache;
        }

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steam = TryGetSteamInstallPath();
        if (string.IsNullOrEmpty(steam))
        {
            _steamCommonRootsCache = roots;
            return roots;
        }

        var steamApps = Path.Combine(steam, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            _steamCommonRootsCache = roots;
            return roots;
        }

        var libraryRoots = new List<string> { steamApps };
        var vdfPath = Path.Combine(steamApps, "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            var text = File.ReadAllText(vdfPath);
            var flat = ParseVdfFlat(text);
            foreach (var kv in flat)
            {
                if (kv.Key.All(char.IsDigit) && Directory.Exists(Path.Combine(kv.Value, "steamapps")))
                {
                    libraryRoots.Add(Path.Combine(kv.Value, "steamapps"));
                }
            }
        }

        foreach (var lib in libraryRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var common = Path.Combine(lib, "common");
            if (Directory.Exists(common))
            {
                roots.Add(Path.GetFullPath(common).ToLowerInvariant());
            }
        }

        _steamCommonRootsCache = roots;
        return roots;
    }

    private bool InstallUnderSteamCommon(string installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return false;
        }

        var n = Path.GetFullPath(installLocation).ToLowerInvariant();
        foreach (var root in GetSteamLibraryCommonRoots())
        {
            if (n.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (n.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SteamCommonJunkPath(string installLower)
    {
        ReadOnlySpan<string> junk =
        [
            @"\steamworks common",
            "steamworks common redistributable",
            @"\tools\",
            @"\directx\",
            @"\_commonredist",
            "dedicated server",
            "redistributables",
            "vc_redist",
            "dotnet",
            "openxr",
            "epic online services"
        ];
        foreach (var j in junk)
        {
            if (installLower.Contains(j, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool InstallHasSteamAppIdMarker(string installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
        {
            return false;
        }

        foreach (var baseDir in new[] { installLocation, Path.GetDirectoryName(Path.GetFullPath(installLocation)) ?? "" })
        {
            if (string.IsNullOrEmpty(baseDir))
            {
                continue;
            }

            if (File.Exists(Path.Combine(baseDir, "steam_appid.txt")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameOrPublisherExcluded(string? displayName, string? publisher)
    {
        var blob = $"{displayName}\n{publisher}";
        return NonGameTitleOrPublisher.IsMatch(blob);
    }

    private static string DetectPlatformFromRegistry(string? displayName, string? publisher, string installLower)
    {
        var pub = (publisher ?? "").ToLowerInvariant();
        var name = (displayName ?? "").ToLowerInvariant();
        if (pub.Contains("gog", StringComparison.Ordinal) || installLower.Contains("gog", StringComparison.Ordinal))
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

        if (pub.Contains("valve", StringComparison.Ordinal) || installLower.Contains("steamapps", StringComparison.Ordinal))
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

        return "PC";
    }

    private static bool PathSuggestsNonGameSoftware(string installLower)
    {
        ReadOnlySpan<string> bad =
        [
            @"\google\chrome",
            @"\mozilla firefox",
            @"\microsoft\edge",
            @"\7-zip",
            @"\videolan\vlc",
            @"\cursor\",
            @"\docker\",
            @"\nodejs\",
            @"\python",
            @"\discord\",
            @"\obs-studio",
        ];
        foreach (var b in bad)
        {
            if (installLower.Contains(b, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLikelyGame(string? displayName, string? publisher, string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        if (!Directory.Exists(installLocation))
        {
            return false;
        }

        var nameLower = displayName.ToLowerInvariant();
        var publisherLower = (publisher ?? "").ToLowerInvariant();
        var installLower = installLocation.ToLowerInvariant();

        if (NameOrPublisherExcluded(displayName, publisher))
        {
            return false;
        }

        if (PathSuggestsNonGameSoftware(installLower))
        {
            return false;
        }

        if (InstallUnderSteamCommon(installLocation))
        {
            if (SteamCommonJunkPath(installLower))
            {
                return false;
            }

            if (nameLower.Contains("redistributable", StringComparison.Ordinal)
                || publisherLower.Contains("redistributable", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        if (InstallHasSteamAppIdMarker(installLocation))
        {
            return !SteamCommonJunkPath(installLower);
        }

        ReadOnlySpan<string> indicators =
        [
            "gog", "epic", "steam", "ubisoft", "electronic arts", "ea ", "blizzard", "bethesda",
            "rockstar", "activision", "square enix", "capcom", "bandai", "paradox", "sega", "konami",
            "namco", "thq", "focus entertainment", "xbox game studios"
        ];
        foreach (var ind in indicators)
        {
            if (publisherLower.Contains(ind, StringComparison.Ordinal)
                || installLower.Contains(ind, StringComparison.Ordinal))
            {
                return true;
            }
        }

        ReadOnlySpan<string> gameFolders = ["\\games\\", "\\game\\", "steamapps", "gog", "epic", "origin", "uplay", "xboxgames"];
        foreach (var folder in gameFolders)
        {
            if (installLower.Contains(folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private List<GameRecord> DetectSteamGames()
    {
        var steamPath = TryGetSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath))
        {
            return [];
        }

        var steamAppsPath = Path.Combine(steamPath, "steamapps");
        if (!Directory.Exists(steamAppsPath))
        {
            return [];
        }

        var libraryPaths = new List<string> { steamAppsPath };
        var libraryFolders = Path.Combine(steamAppsPath, "libraryfolders.vdf");
        if (File.Exists(libraryFolders))
        {
            var flat = ParseVdfFlat(File.ReadAllText(libraryFolders));
            foreach (var kv in flat)
            {
                if (kv.Key.All(char.IsDigit))
                {
                    var candidate = Path.Combine(kv.Value, "steamapps");
                    if (Directory.Exists(candidate))
                    {
                        libraryPaths.Add(candidate);
                    }
                }
            }
        }

        var found = new List<GameRecord>();
        foreach (var libPath in libraryPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(libPath))
            {
                continue;
            }

            foreach (var acfFile in Directory.EnumerateFiles(libPath, "appmanifest_*.acf"))
            {
                try
                {
                    var acf = ParseVdfFlat(File.ReadAllText(acfFile));
                    acf.TryGetValue("appid", out var appId);
                    acf.TryGetValue("name", out var gameName);
                    acf.TryGetValue("installdir", out var installDir);
                    acf.TryGetValue("StateFlags", out var state);

                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(installDir))
                    {
                        continue;
                    }

                    if (state != "4")
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(libPath, "common", installDir);
                    if (!Directory.Exists(fullPath))
                    {
                        continue;
                    }

                    found.Add(new GameRecord(
                        gameName,
                        appId,
                        fullPath,
                        "Steam",
                        GameDisplayName.CleanDisplayName(gameName)));
                }
                catch
                {
                    // skip malformed manifest
                }
            }
        }

        return DedupeSteamSharedInstallFolder(found);
    }

    /// <summary>Merge rows that share the same install directory (DLC IDs); keep lowest numeric app id.</summary>
    private static List<GameRecord> DedupeSteamSharedInstallFolder(List<GameRecord> games)
    {
        var groups = games.GroupBy(g => Path.GetFullPath(g.InstallPath ?? "").ToLowerInvariant());
        var result = new List<GameRecord>();
        foreach (var g in groups)
        {
            var list = g.ToList();
            if (list.Count == 1)
            {
                result.Add(list[0]);
                continue;
            }

            GameRecord? best = null;
            foreach (var row in list)
            {
                if (!int.TryParse(row.AppId, out var id))
                {
                    id = int.MaxValue;
                }

                if (best is null)
                {
                    best = row;
                    continue;
                }

                if (!int.TryParse(best.AppId, out var bestId))
                {
                    bestId = int.MaxValue;
                }

                if (id < bestId || (id == bestId && string.Compare(row.Name, best.Name, StringComparison.Ordinal) < 0))
                {
                    best = row;
                }
            }

            if (best is not null)
            {
                result.Add(best);
            }
        }

        return result;
    }

    private List<Dictionary<string, string>> DetectRegistryGames()
    {
        if (_registryGamesCache is not null)
        {
            return _registryGamesCache;
        }

        var found = new List<Dictionary<string, string>>();
        var roots = new (RegistryKey Root, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (root, path) in roots)
        {
            try
            {
                using var uninstall = root.OpenSubKey(path);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = uninstall.OpenSubKey(subName);
                        if (sub is null)
                        {
                            continue;
                        }

                        var displayName = sub.GetValue("DisplayName") as string;
                        var publisher = sub.GetValue("Publisher") as string;
                        var installLocation = sub.GetValue("InstallLocation") as string;
                        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
                        {
                            continue;
                        }

                        installLocation = Environment.ExpandEnvironmentVariables(installLocation).Trim().TrimEnd('\\');
                        if (!IsLikelyGame(displayName, publisher, installLocation))
                        {
                            continue;
                        }

                        var platform = DetectPlatformFromRegistry(displayName, publisher, installLocation.ToLowerInvariant());
                        found.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = displayName,
                            ["app_id"] = "",
                            ["install_path"] = installLocation.Trim(),
                            ["platform"] = platform
                        });
                    }
                    catch
                    {
                        // skip bad key
                    }
                }
            }
            catch
            {
                // skip hive
            }
        }

        _registryGamesCache = found;
        return found;
    }
}
