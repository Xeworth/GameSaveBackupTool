using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using GSBT.Core.Common;
using GSBT.Core.Models;
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
    public static bool IsRegistryTargetSafe(string hive, string subkey)
    {
        if (!IsSubkeySafeForExport(subkey) || !TryOpenKey(hive, subkey, out var key))
        {
            return false;
        }

        key?.Dispose();
        return true;
    }

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
        => TryComputeSnapshotFingerprint(hive, subkey, out fingerprintHex, out _);

    public static bool TryComputeSnapshotFingerprint(
        string hive,
        string subkey,
        out string fingerprintHex,
        out string? error)
    {
        fingerprintHex = string.Empty;
        error = null;
        if (!TryOpenKey(hive, subkey, out var key))
        {
            error = "Registry save key is not available.";
            return false;
        }

        using (key!)
        {
            try
            {
                var sb = new StringBuilder(256);
                AppendKeyFingerprint(sb, key!, string.Empty);
                if (sb.Length == 0)
                {
                    error = "Registry save key contains no readable values.";
                    return false;
                }

                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                fingerprintHex = Convert.ToHexString(SHA256.HashData(bytes));
                return true;
            }
            catch (Exception ex)
            {
                error = $"Registry save key could not be read completely: {ex.Message}";
                return false;
            }
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
        var result = BackupToRetentionFileWithResult(
            gameName,
            hive,
            subkey,
            backupRoot,
            retentionCount,
            subfolderPerGame,
            cancellationToken);
        backupFilePath = result.BackupPath;
        error = result.Success ? null : result.Error ?? "Registry backup did not complete.";
    }

    public BackupOperationResult BackupToRetentionFileWithResult(
        string gameName,
        string hive,
        string subkey,
        string backupRoot,
        int retentionCount,
        bool subfolderPerGame,
        CancellationToken cancellationToken)
    {
        var displayName = string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName.Trim();
        var registrySource = TryToRegExportPath(hive, subkey, out var canonicalRegistrySource)
            ? canonicalRegistrySource
            : RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey);
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            return RegistryFailure(displayName, registrySource, "Backup destination is not set.");
        }

        if (!IsSubkeySafeForExport(subkey) || !TryOpenKey(hive, subkey, out var key))
        {
            return RegistryFailure(displayName, registrySource, "Registry save key is not available or is unsafe.");
        }

        key?.Dispose();
        string root;
        try
        {
            root = BackupPathSafety.NormalizeDirectory(backupRoot);
            Directory.CreateDirectory(root);
        }
        catch (Exception ex)
        {
            return RegistryFailure(displayName, registrySource, ex.Message);
        }

        var safe = SanitizeGameFolderName(displayName);
        var baseDir = subfolderPerGame ? Path.Combine(root, safe) : root;
        var runId = Guid.NewGuid().ToString("N");
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_'at'_HH-mm-ss-fff");
        var backupRunDir = Path.Combine(baseDir, $"{safe} - Backup {stamp}-{runId[..8]}");
        var stagingDir = Path.Combine(baseDir, $".gsbt-staging-{runId}");
        var stagingFile = Path.Combine(stagingDir, $"{safe}.reg");
        var finalFile = Path.Combine(backupRunDir, $"{safe}.reg");

        try
        {
            using var rootLease = OperationFileLease.Acquire(
                Path.Combine(root, ".gsbt-operation.lock"),
                TimeSpan.FromMinutes(10),
                cancellationToken);
            using var operationLock = CrossProcessLock.Acquire($"backup:{baseDir}:{displayName}", TimeSpan.FromMinutes(10));
            Directory.CreateDirectory(stagingDir);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryExportKeyToRegFile(hive, subkey, stagingFile, cancellationToken, out var exportError))
            {
                TryDeleteRegistryStaging(stagingDir);
                return RegistryFailure(displayName, registrySource, exportError ?? "Registry export failed.", runId: runId);
            }

            if (!LooksLikeRegistryExport(stagingFile))
            {
                TryDeleteRegistryStaging(stagingDir);
                return RegistryFailure(displayName, registrySource, "Registry export did not contain a valid .reg header.", runId: runId);
            }

            Directory.Move(stagingDir, backupRunDir);
            if (!BackupRunManifestStore.TryWriteManifest(
                    displayName,
                    registrySource,
                    backupRunDir,
                    out var checkpointError,
                    sourceIsRegistry: true))
            {
                return new BackupOperationResult
                {
                    GameName = displayName,
                    Status = BackupOperationStatus.Partial,
                    Source = registrySource,
                    BackupPath = finalFile,
                    RunId = runId,
                    IsRegistry = true,
                    FilesCopied = 1,
                    BytesCopied = new FileInfo(finalFile).Length,
                    Error = $"Registry export was created, but verification metadata could not be finalized: {checkpointError}",
                };
            }

            var warnings = PruneOldRegistryBackups(baseDir, safe, displayName, Math.Max(1, retentionCount));
            try
            {
                DeleteLegacyFlatRegExports(baseDir, safe);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not remove a legacy registry export: {ex.Message}");
            }

            return new BackupOperationResult
            {
                GameName = displayName,
                Status = BackupOperationStatus.Succeeded,
                Source = registrySource,
                BackupPath = finalFile,
                RunId = runId,
                IsRegistry = true,
                FilesCopied = 1,
                BytesCopied = new FileInfo(finalFile).Length,
                Warnings = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteRegistryStaging(stagingDir);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteRegistryStaging(stagingDir);
            return RegistryFailure(displayName, registrySource, ex.Message, finalFile, runId);
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

            cancellationToken.ThrowIfCancellationRequested();

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

    private static List<string> PruneOldRegistryBackups(
        string baseDir,
        string safeName,
        string gameName,
        int retentionCount)
    {
        var warnings = new List<string>();
        if (retentionCount <= 0 || !Directory.Exists(baseDir))
        {
            return warnings;
        }

        var prefix = $"{safeName} - Backup";
        var candidates = Directory.EnumerateDirectories(baseDir)
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal))
            .Select(d => new
            {
                Path = d,
                Manifest = BackupRunManifestStore.TryReadManifest(d, out var manifest) ? manifest : null,
            })
            .ToList();

        var hasDifferentOwner = candidates.Any(x =>
            x.Manifest is not null
            && !string.Equals(x.Manifest.GameName, gameName, StringComparison.OrdinalIgnoreCase));
        var owned = candidates
            .Where(x => x.Manifest is not null
                ? string.Equals(x.Manifest.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                : !hasDifferentOwner)
            .OrderBy(x => Directory.GetLastWriteTimeUtc(x.Path))
            .ToList();

        while (owned.Count > retentionCount)
        {
            var oldest = owned[0].Path;
            owned.RemoveAt(0);
            try
            {
                Directory.Delete(oldest, recursive: true);
                BackupRunManifestStore.DeleteManifestForBackupRun(oldest);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not prune old registry backup '{Path.GetFileName(oldest)}': {ex.Message}");
                break;
            }
        }

        return warnings;
    }

    private static bool LooksLikeRegistryExport(string path)
    {
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var first = reader.ReadLine();
            return first?.StartsWith("Windows Registry Editor Version", StringComparison.OrdinalIgnoreCase) == true
                || first?.StartsWith("REGEDIT4", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteRegistryStaging(string path)
    {
        try
        {
            if (Path.GetFileName(path).StartsWith(".gsbt-staging-", StringComparison.Ordinal)
                && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // The unique staging path cannot replace a prior valid backup.
        }
    }

    private static BackupOperationResult RegistryFailure(
        string gameName,
        string source,
        string error,
        string backupPath = "",
        string runId = "") =>
        new()
        {
            GameName = gameName,
            Status = BackupOperationStatus.Failed,
            Source = source,
            BackupPath = backupPath,
            RunId = runId,
            IsRegistry = true,
            Error = error,
        };

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
