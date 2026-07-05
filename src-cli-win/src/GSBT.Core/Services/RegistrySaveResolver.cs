using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using GSBT.Core.Common;
using Microsoft.Win32;

namespace GSBT.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class RegistrySaveResolver
{
    private static readonly Regex RegistryLine = new(
        @"(?is)\b(HKEY_(?:CURRENT_USER|LOCAL_MACHINE|USERS|CLASSES_ROOT)\s*[\\/][^;\n\r|]+|(?:HKCU|HKLM|HKCR|HKU)\s*[\\/][^;\n\r|]+)",
        RegexOptions.Compiled);

    private static readonly string[] ValueNamesToTry =
    [
        "SavePath", "Savegame", "SaveDirectory", "Save Dir", "SaveDir", "SaveGame", "PlayerSaveDir", "InstallPath", "Path", "SaveLocation", "DataPath", ""
    ];

    private readonly Lazy<Dictionary<string, (string Hive, string Subkey)>> _mergedSteamRegistrySaveKeys;

    public RegistrySaveResolver(string? configRoot = null)
    {
        _mergedSteamRegistrySaveKeys = new(() => LoadMergedSteamRegistrySaveKeys(configRoot));
    }

    public IReadOnlyDictionary<string, (string Hive, string Subkey)> MergedSteamRegistrySaveKeys => _mergedSteamRegistrySaveKeys.Value;

    public static string NormalizeRegistryPastedPath(string text)
        => Regex.Replace((text ?? string.Empty).Trim(), @"(?i)^Computer\\+", string.Empty);

    public static bool LooksLikeRegistryHiveLine(string text)
    {
        var s = NormalizeRegistryPastedPath(text);
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var up = s.ToUpperInvariant();
        return up.StartsWith("HKEY_") || up.StartsWith("HKCU") || up.StartsWith("HKLM") || up.StartsWith("HKCR") || up.StartsWith("HKU") || RegistryLine.IsMatch(s);
    }

    /// <summary>Detects pasted folder paths mistaken for registry paths (e.g. <c>C:\Games\…</c>).</summary>
    public static bool LooksLikeFilesystemPath(string text)
    {
        var t = (text ?? string.Empty).Trim().Trim('"');
        if (t.Length == 0)
        {
            return false;
        }

        if (t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (t.Length >= 2 && char.IsLetter(t[0]) && t[1] == ':')
        {
            return true;
        }

        if (t.StartsWith(@"\\", StringComparison.Ordinal) || t.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        if (t.StartsWith('\\') && !LooksLikeRegistryHiveLine(t))
        {
            return true;
        }

        return false;
    }

    public enum RegistrySaveValidationKind
    {
        ValidFolder,
        ValidInKey,
        FilesystemPath,
        NotRegistrySyntax,
        UnknownHive,
        MissingSubkeyPath,
        KeyNotFound,
        KeyEmpty,
        FolderTargetMissing,
    }

    public readonly record struct RegistrySaveValidation(
        RegistrySaveValidationKind Kind,
        string Message,
        string? FolderRaw = null,
        string? FolderResolved = null,
        string? Hive = null,
        string? Subkey = null)
    {
        public bool IsSuccess =>
            Kind is RegistrySaveValidationKind.ValidFolder or RegistrySaveValidationKind.ValidInKey;
    }

    /// <summary>Validates a single registry hint before assigning a save location (folder or in-key).</summary>
    public RegistrySaveValidation ValidateRegistrySaveHint(string hint)
    {
        var normalized = NormalizeRegistryPastedPath(hint);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new(RegistrySaveValidationKind.NotRegistrySyntax, "Enter a registry path or paste from Regedit.");
        }

        if (LooksLikeFilesystemPath(normalized))
        {
            return new(
                RegistrySaveValidationKind.FilesystemPath,
                "That looks like a file or folder path, not a registry key. Copy the path from Regedit’s address bar (e.g. HKCU\\Software\\…).");
        }

        if (!LooksLikeRegistryHiveLine(normalized))
        {
            return new(
                RegistrySaveValidationKind.NotRegistrySyntax,
                "Could not parse a registry path. Example: HKCU\\Software\\YourGame");
        }

        var (hive, remainder) = SplitHiveAndRemainder(normalized);
        if (hive is null)
        {
            if (IsKnownHiveTokenOnly(normalized))
            {
                return new(
                    RegistrySaveValidationKind.MissingSubkeyPath,
                    "Include the full key path after the hive (e.g. HKCU\\Software\\YourGame).");
            }

            return new(
                RegistrySaveValidationKind.UnknownHive,
                "Unrecognized registry hive. Use HKCU, HKLM, HKU, or HKCR.");
        }

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return new(
                RegistrySaveValidationKind.MissingSubkeyPath,
                "Include the full key path after the hive (e.g. HKCU\\Software\\YourGame).");
        }

        var folder = ResolveRegistryHintToSaveFolder(hint);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(folder.Trim());
                var resolved = Path.GetFullPath(expanded);
                if (!Directory.Exists(resolved))
                {
                    return new(
                        RegistrySaveValidationKind.FolderTargetMissing,
                        $"Registry points to a folder that does not exist:\n{resolved}",
                        FolderRaw: folder.Trim(),
                        FolderResolved: resolved);
                }

                return new(
                    RegistrySaveValidationKind.ValidFolder,
                    string.Empty,
                    FolderRaw: folder.Trim(),
                    FolderResolved: resolved);
            }
            catch (Exception ex)
            {
                return new(
                    RegistrySaveValidationKind.FolderTargetMissing,
                    $"Registry points to an invalid folder path: {ex.Message}",
                    FolderRaw: folder.Trim());
            }
        }

        using var key = hive.OpenSubKey(remainder, writable: false);
        if (key is null)
        {
            return new(
                RegistrySaveValidationKind.KeyNotFound,
                "Registry key not found. In Regedit, select the key (folder icon), not a single value, and copy the path from the address bar.");
        }

        if (key.ValueCount < 1 && key.SubKeyCount < 1)
        {
            return new(
                RegistrySaveValidationKind.KeyEmpty,
                "That registry key exists but has no values or subkeys to back up.");
        }

        return new(
            RegistrySaveValidationKind.ValidInKey,
            string.Empty,
            Hive: HiveName(hive),
            Subkey: remainder);
    }

    public static IReadOnlyList<string> ExtractRegistryHints(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var cleaned = NormalizeRegistryPastedPath(text);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (Match m in RegistryLine.Matches(cleaned))
        {
            var hint = m.Groups[1].Value.Trim().Trim('"').Replace('/', '\\');
            if (seen.Add(hint))
            {
                list.Add(hint);
            }
        }

        return list;
    }

    public static string FormatRegistrySaveDisplay(string hive, string subkey) => $"{hive}\\{subkey}";

    public string? ResolveRegistryHintToSaveFolder(string hint)
    {
        var normalized = NormalizeRegistryPastedPath(hint);
        var (hive, remainder) = SplitHiveAndRemainder(normalized);
        if (hive is null || string.IsNullOrWhiteSpace(remainder))
        {
            return null;
        }

        var parts = remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var depth = parts.Length; depth >= 1; depth--)
        {
            var keyPath = string.Join('\\', parts.Take(depth));
            var valueSegments = parts.Skip(depth).ToArray();
            using var key = hive.OpenSubKey(keyPath, writable: false);
            if (key is null)
            {
                continue;
            }

            if (valueSegments.Length > 0)
            {
                var fromValue = ValueToFolder(key.GetValue(valueSegments[0]));
                if (!string.IsNullOrWhiteSpace(fromValue))
                {
                    return fromValue;
                }
            }

            foreach (var vn in ValueNamesToTry)
            {
                var folder = ValueToFolder(key.GetValue(vn));
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    return folder;
                }
            }
        }

        return null;
    }

    public (string Hive, string Subkey)? TryRegistryKeyAsInKeySaveLocation(string hint)
    {
        var normalized = NormalizeRegistryPastedPath(hint);
        var (hive, remainder) = SplitHiveAndRemainder(normalized);
        if (hive is null || string.IsNullOrWhiteSpace(remainder))
        {
            return null;
        }

        using var key = hive.OpenSubKey(remainder, writable: false);
        if (key is null)
        {
            return null;
        }

        if (key.ValueCount < 1 && key.SubKeyCount < 1)
        {
            return null;
        }

        return (HiveName(hive), remainder);
    }

    private static Dictionary<string, (string Hive, string Subkey)> LoadMergedSteamRegistrySaveKeys(string? configRoot)
    {
        var outMap = new Dictionary<string, (string Hive, string Subkey)>(StringComparer.OrdinalIgnoreCase);

        var root = configRoot ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var jsonPath = Path.Combine(root, "config", "steam_registry_save_keys.json");
        if (!File.Exists(jsonPath))
        {
            return outMap;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return outMap;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var hive = prop.Value.TryGetProperty("hive", out var h) ? h.GetString() : null;
                var sub = prop.Value.TryGetProperty("subkey", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(hive) || string.IsNullOrWhiteSpace(sub))
                {
                    continue;
                }

                outMap[prop.Name] = (hive.Trim(), sub.Replace('/', '\\'));
            }
        }
        catch
        {
            // Best effort parse.
        }

        return outMap;
    }

    private static string? ValueToFolder(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string[] arr && arr.Length > 0)
        {
            return ValueToFolder(arr[0]);
        }

        if (value is not string raw)
        {
            return null;
        }

        var s = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (File.Exists(s))
        {
            s = Path.GetDirectoryName(s) ?? s;
        }

        try
        {
            s = Path.GetFullPath(s);
        }
        catch
        {
            return null;
        }

        if (!Directory.Exists(s))
        {
            return null;
        }

        var candidate = s.EndsWith('\\') ? s : $"{s}\\";
        return PathUtils.PathToDirectoryOnly(candidate);
    }

    private static string HiveName(RegistryKey key)
    {
        if (ReferenceEquals(key, Registry.CurrentUser))
        {
            return "HKEY_CURRENT_USER";
        }

        if (ReferenceEquals(key, Registry.LocalMachine))
        {
            return "HKEY_LOCAL_MACHINE";
        }

        if (ReferenceEquals(key, Registry.Users))
        {
            return "HKEY_USERS";
        }

        if (ReferenceEquals(key, Registry.ClassesRoot))
        {
            return "HKEY_CLASSES_ROOT";
        }

        return "HKEY_UNKNOWN";
    }

    private static bool IsKnownHiveTokenOnly(string normalized)
    {
        var token = normalized.Trim().TrimEnd('\\');
        return token.ToUpperInvariant() switch
        {
            "HKCU" or "HKLM" or "HKCR" or "HKU" => true,
            "HKEY_CURRENT_USER" or "HKEY_LOCAL_MACHINE" or "HKEY_USERS" or "HKEY_CLASSES_ROOT" => true,
            _ => false,
        };
    }

    private static (RegistryKey? Hive, string Remainder) SplitHiveAndRemainder(string hint)
    {
        var normalized = hint.Trim().Trim('"').Replace('/', '\\');
        var mappings = new (string Prefix, RegistryKey Hive)[]
        {
            ("HKEY_CURRENT_USER\\", Registry.CurrentUser),
            ("HKCU\\", Registry.CurrentUser),
            ("HKEY_LOCAL_MACHINE\\", Registry.LocalMachine),
            ("HKLM\\", Registry.LocalMachine),
            ("HKEY_USERS\\", Registry.Users),
            ("HKU\\", Registry.Users),
            ("HKEY_CLASSES_ROOT\\", Registry.ClassesRoot),
            ("HKCR\\", Registry.ClassesRoot)
        };

        foreach (var (prefix, hive) in mappings)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (hive, normalized[prefix.Length..].TrimStart('\\'));
            }
        }

        return (null, string.Empty);
    }
}
