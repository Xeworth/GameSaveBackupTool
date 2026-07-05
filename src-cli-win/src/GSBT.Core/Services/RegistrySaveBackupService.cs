using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using GSBT.Core.Common;
using Microsoft.Win32;

namespace GSBT.Core.Services;

/// <summary>Exports in-registry saves to timestamped <c>.reg</c> files with retention (folder-backup parity).</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistrySaveBackupService
{
    public readonly record struct RegistrySaveTarget(string Hive, string Subkey);

    /// <summary>Reads <c>save_registry_hive</c> / <c>save_registry_subkey</c> when <c>save_in_registry_only</c> is true.</summary>
    public static bool TryGetTargetFromCatalogRow(
        IReadOnlyDictionary<string, object?> row,
        out RegistrySaveTarget target)
    {
        target = default;
        if (!row.TryGetValue("save_in_registry_only", out var ro)
            || !bool.TryParse(ro?.ToString(), out var regOnly)
            || !regOnly)
        {
            return false;
        }

        var hive = row.TryGetValue("save_registry_hive", out var h) ? h?.ToString()?.Trim() : null;
        var subkey = row.TryGetValue("save_registry_subkey", out var s) ? s?.ToString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(hive) || string.IsNullOrWhiteSpace(subkey))
        {
            return false;
        }

        var normalizedSubkey = subkey.Replace('/', '\\');
        if (!IsSubkeySafeForExport(normalizedSubkey))
        {
            return false;
        }

        target = new RegistrySaveTarget(hive, normalizedSubkey);
        return true;
    }

    /// <summary>Re-validates hive + subkey before auto-backup (catalog may be hand-edited).</summary>
    public static bool IsRegistryTargetSafe(string hive, string subkey) =>
        IsSubkeySafeForExport(subkey) && TryOpenKey(hive, subkey, out _);

    private static bool IsSubkeySafeForExport(string subkey)
    {
        if (string.IsNullOrWhiteSpace(subkey) || subkey.Length > 512)
        {
            return false;
        }

        if (subkey.IndexOfAny(['"', '\0', '\n', '\r']) >= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>Stable fingerprint of all values under the key (used for poll-based change detection).</summary>
    public static bool TryComputeSnapshotFingerprint(string hive, string subkey, out string fingerprintHex)
    {
        fingerprintHex = string.Empty;
        if (!TryOpenKey(hive, subkey, out var key))
        {
            return false;
        }

        using (key!)
        {
            var sb = new StringBuilder(256);
            AppendKeyFingerprint(sb, key!, string.Empty);
            if (sb.Length == 0)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            fingerprintHex = Convert.ToHexString(SHA256.HashData(bytes));
            return true;
        }
    }

    public string SanitizeGameFolderName(string gameName) =>
        GameNameInputValidation.SanitizeForWindowsPathSegment(gameName);

    /// <summary>Exports the registry subtree into a timestamped retention folder (same layout as <see cref="SaveFolderBackupService"/>).</summary>
    public void BackupToRetentionFile(
        string gameName,
        string hive,
        string subkey,
        string backupRoot,
        int retentionCount,
        bool subfolderPerGame,
        CancellationToken cancellationToken,
        out string backupFilePath,
        out string? error)
    {
        backupFilePath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            error = "Backup destination is not set.";
            return;
        }

        try
        {
            Directory.CreateDirectory(backupRoot);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return;
        }

        if (!TryOpenKey(hive, subkey, out _))
        {
            error = "Registry save key is not available.";
            return;
        }

        var safe = SanitizeGameFolderName(string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);
        var baseDir = subfolderPerGame
            ? Path.Combine(backupRoot, safe)
            : backupRoot;

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return;
        }

        try
        {
            var removed = PruneOldRegistryBackups(baseDir, safe, retentionCount);
            foreach (var removedPath in removed)
            {
                BackupRunManifestStore.DeleteManifestForBackupRun(removedPath);
            }
        }
        catch
        {
            // best-effort retention
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_at_HH-mm-ss-fff");
        var backupRunDir = Path.Combine(baseDir, $"{safe} - Backup {stamp}");
        try
        {
            Directory.CreateDirectory(backupRunDir);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return;
        }

        backupFilePath = Path.Combine(backupRunDir, $"{safe}.reg");

        if (!TryExportKeyToRegFile(hive, subkey, backupFilePath, cancellationToken, out var exportErr))
        {
            error = exportErr;
            try
            {
                if (Directory.Exists(backupRunDir))
                {
                    Directory.Delete(backupRunDir, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }
        else
        {
            try
            {
                DeleteLegacyFlatRegExports(baseDir, safe);
            }
            catch
            {
                // best-effort migration from older flat .reg layout
            }

            BackupRunManifestStore.TryWriteManifest(
                gameName,
                RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
                backupRunDir);
        }
    }

    private static bool TryExportKeyToRegFile(
        string hive,
        string subkey,
        string regFilePath,
        CancellationToken cancellationToken,
        out string? error)
    {
        error = null;
        if (!TryToRegExportPath(hive, subkey, out var exportPath))
        {
            error = "Unsupported registry hive.";
            return false;
        }

        try
        {
            var dir = Path.GetDirectoryName(regFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(regFilePath))
            {
                File.Delete(regFilePath);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add("export");
            psi.ArgumentList.Add(exportPath);
            psi.ArgumentList.Add(regFilePath);
            psi.ArgumentList.Add("/y");
            using var proc = Process.Start(psi);

            if (proc is null)
            {
                error = "Could not start reg.exe.";
                return false;
            }

            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           if (!proc.HasExited)
                           {
                               proc.Kill(entireProcessTree: true);
                           }
                       }
                       catch
                       {
                           // ignore
                       }
                   }))
            {
                proc.WaitForExit();
            }

            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                error = string.IsNullOrWhiteSpace(err)
                    ? $"reg export failed (exit {proc.ExitCode})."
                    : err.Trim();
                return false;
            }

            if (!File.Exists(regFilePath) || new FileInfo(regFilePath).Length == 0)
            {
                error = "Registry export produced no file.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static List<string> PruneOldRegistryBackups(string baseDir, string safeName, int retentionCount)
    {
        var removed = new List<string>();
        if (retentionCount <= 0 || !Directory.Exists(baseDir))
        {
            return removed;
        }

        var prefix = $"{safeName} - Backup";
        var dirs = Directory.EnumerateDirectories(baseDir)
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal))
            .Select(d => new DirectoryInfo(d))
            .OrderBy(d => d.LastWriteTimeUtc)
            .ToList();

        while (dirs.Count >= retentionCount)
        {
            var oldest = dirs[0].FullName;
            dirs.RemoveAt(0);
            try
            {
                Directory.Delete(oldest, recursive: true);
                removed.Add(oldest);
            }
            catch
            {
                break;
            }
        }

        return removed;
    }

    /// <summary>Removes pre-subfolder exports (<c>{Game} - Backup *.reg</c> directly under the game folder).</summary>
    private static void DeleteLegacyFlatRegExports(string baseDir, string safeName)
    {
        if (!Directory.Exists(baseDir))
        {
            return;
        }

        var prefix = $"{safeName} - Backup";
        foreach (var file in Directory.EnumerateFiles(baseDir, $"{prefix}*.reg"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static bool TryToRegExportPath(string hive, string subkey, out string exportPath)
    {
        exportPath = string.Empty;
        var hiveToken = hive.Trim().ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKEY_CURRENT_USER",
            "HKCU" => "HKEY_CURRENT_USER",
            "HKEY_LOCAL_MACHINE" => "HKEY_LOCAL_MACHINE",
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKEY_USERS" => "HKEY_USERS",
            "HKU" => "HKEY_USERS",
            "HKEY_CLASSES_ROOT" => "HKEY_CLASSES_ROOT",
            "HKCR" => "HKEY_CLASSES_ROOT",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(hiveToken))
        {
            return false;
        }

        exportPath = $"{hiveToken}\\{subkey.Trim().TrimStart('\\')}";
        return true;
    }

    private static bool TryOpenKey(string hive, string subkey, out RegistryKey? key)
    {
        key = null;
        RegistryKey? root = hive.Trim().ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_USERS" or "HKU" => Registry.Users,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            _ => null,
        };

        if (root is null)
        {
            return false;
        }

        try
        {
            key = root.OpenSubKey(subkey.Trim().TrimStart('\\'), writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendKeyFingerprint(StringBuilder sb, RegistryKey key, string pathPrefix)
    {
        foreach (var valueName in key.GetValueNames().OrderBy(static n => n, StringComparer.Ordinal))
        {
            var value = key.GetValue(valueName);
            sb.Append(pathPrefix).Append('\\').Append(valueName).Append('=');
            AppendValueFingerprint(sb, value);
            sb.Append(';');
        }

        foreach (var child in key.GetSubKeyNames().OrderBy(static n => n, StringComparer.Ordinal))
        {
            using var sub = key.OpenSubKey(child, writable: false);
            if (sub is null)
            {
                continue;
            }

            var childPrefix = string.IsNullOrEmpty(pathPrefix) ? child : $"{pathPrefix}\\{child}";
            AppendKeyFingerprint(sb, sub, childPrefix);
        }
    }

    private static void AppendValueFingerprint(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append("s:").Append(s);
                break;
            case int i:
                sb.Append("i:").Append(i);
                break;
            case long l:
                sb.Append("l:").Append(l);
                break;
            case byte[] bytes:
                sb.Append("b:").Append(Convert.ToHexString(bytes));
                break;
            case string[] arr:
                sb.Append('[');
                for (var i = 0; i < arr.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append(arr[i]);
                }

                sb.Append(']');
                break;
            default:
                sb.Append(value.ToString());
                break;
        }
    }
}
