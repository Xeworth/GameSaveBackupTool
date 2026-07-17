using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>
/// Persists per–backup-run checkpoints under <c>%AppData%\Game Save Backup Tool\winui\backup_run_checkpoints\</c> (hidden from the backup folder itself).
/// </summary>
public static class BackupRunManifestStore
{
    private const string CheckpointsSubDir = "backup_run_checkpoints";
    private static readonly AsyncLocal<string?> CheckpointsRootOverride = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string GetCheckpointsRootDirectory() =>
        CheckpointsRootOverride.Value
        ?? Path.Combine(UserDataDir.GetWinUiUserDataDir(), CheckpointsSubDir);

    internal static IDisposable UseCheckpointsRootForTests(string root)
    {
        var previous = CheckpointsRootOverride.Value;
        CheckpointsRootOverride.Value = Path.GetFullPath(root);
        return new TestRootScope(previous);
    }

    public static string GetStoragePathForBackupRun(string backupRunFullPath)
    {
        var normalized = NormalizeBackupRunPath(backupRunFullPath);
        var hash = Sha256HexLower(normalized);
        return Path.Combine(GetCheckpointsRootDirectory(), $"{hash}.json");
    }

    /// <summary>Writes a content-aware checkpoint after a successful backup.</summary>
    public static bool TryWriteManifest(string gameName, string sourceSaveDirectory, string backupRunDirectory) =>
        TryWriteManifest(gameName, sourceSaveDirectory, backupRunDirectory, out _);

    public static bool TryWriteManifest(
        string gameName,
        string sourceSaveDirectory,
        string backupRunDirectory,
        out string? error,
        bool sourceIsRegistry = false)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(backupRunDirectory) || !Directory.Exists(backupRunDirectory))
            {
                error = "Backup run folder is not available.";
                return false;
            }

            Directory.CreateDirectory(GetCheckpointsRootDirectory());
            var runNorm = NormalizeBackupRunPath(backupRunDirectory);
            var manifest = new BackupRunCheckpointManifest
            {
                RunId = Guid.NewGuid().ToString("N"),
                WriterVersion = AppVersionInfo.RawVersion,
                GameName = gameName ?? string.Empty,
                BackupRunDirectory = runNorm,
                SourceSaveDirectory = string.IsNullOrWhiteSpace(sourceSaveDirectory)
                    ? string.Empty
                    : sourceIsRegistry
                        ? sourceSaveDirectory.Trim()
                        : NormalizeBackupRunPath(sourceSaveDirectory),
                IsRegistry = sourceIsRegistry,
                CheckpointCapturedAtUtc = DateTime.UtcNow.ToString("O")
            };

            foreach (var file in EnumerateFilesStrict(backupRunDirectory))
            {
                try
                {
                    var rel = Path.GetRelativePath(backupRunDirectory, file);
                    var fi = new FileInfo(file);
                    manifest.Files.Add(new BackupRunCheckpointFileEntry
                    {
                        RelativePath = rel,
                        SizeBytes = fi.Length,
                        Extension = fi.Extension ?? string.Empty,
                        FileAttributes = fi.Attributes.ToString(),
                        CreatedTimeUtc = fi.CreationTimeUtc.ToString("O"),
                        LastWriteTimeUtc = fi.LastWriteTimeUtc.ToString("O"),
                        ContentHashSha256 = ComputeFileHash(file)
                    });
                }
                catch (Exception ex)
                {
                    error = $"Could not checkpoint '{file}': {ex.Message}";
                    return false;
                }
            }

            var dest = GetStoragePathForBackupRun(backupRunDirectory);
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            AtomicFileWrite.WriteAllText(dest, json);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryReadManifest(string backupRunFullPath, out BackupRunCheckpointManifest manifest)
    {
        manifest = null!;
        try
        {
            var path = GetStoragePathForBackupRun(backupRunFullPath);
            if (!File.Exists(path))
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize<BackupRunCheckpointManifest>(File.ReadAllText(path), JsonOptions);
            if (parsed is null || !PathsEqual(parsed.BackupRunDirectory, backupRunFullPath))
            {
                return false;
            }

            manifest = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteManifestForBackupRun(string backupRunFullPath)
    {
        try
        {
            var p = GetStoragePathForBackupRun(backupRunFullPath);
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Removes checkpoint files whose <see cref="BackupRunCheckpointManifest.BackupRunDirectory"/> no longer exists on disk.</summary>
    public static void PruneOrphanManifestFiles()
    {
        var root = GetCheckpointsRootDirectory();
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var text = File.ReadAllText(path);
                var doc = JsonSerializer.Deserialize<BackupRunCheckpointManifest>(text, JsonOptions);
                if (doc is null || string.IsNullOrWhiteSpace(doc.BackupRunDirectory))
                {
                    File.Delete(path);
                    continue;
                }

                if (!Directory.Exists(doc.BackupRunDirectory)
                    && IsStorageRootAvailable(doc.BackupRunDirectory))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    /// <summary>True if a checkpoint exists and the fast verification detects missing, changed, or extra files.</summary>
    public static bool HasManifestDrift(string backupRunFullPath)
    {
        if (!TryReadManifest(backupRunFullPath, out _))
        {
            return false;
        }

        return !Verify(backupRunFullPath, BackupVerificationMode.Fast).IsValid;
    }

    public static BackupVerificationResult Verify(string backupRunFullPath, BackupVerificationMode mode)
    {
        var issues = new List<BackupVerificationIssue>();
        if (!Directory.Exists(backupRunFullPath))
        {
            issues.Add(new BackupVerificationIssue(string.Empty, "missing-run", "Backup run folder is unavailable."));
            return new BackupVerificationResult
            {
                BackupPath = backupRunFullPath,
                Mode = mode,
                CheckpointFound = false,
                Issues = issues,
            };
        }

        if (!TryReadManifest(backupRunFullPath, out var doc))
        {
            issues.Add(new BackupVerificationIssue(string.Empty, "missing-checkpoint", "No valid checkpoint exists for this backup run."));
            return new BackupVerificationResult
            {
                BackupPath = backupRunFullPath,
                Mode = mode,
                CheckpointFound = false,
                Issues = issues,
            };
        }

        var expected = new Dictionary<string, BackupRunCheckpointFileEntry>(StringComparer.OrdinalIgnoreCase);
        var checkedFiles = 0;
        foreach (var entry in doc.Files ?? [])
        {
            if (!TryResolveManifestEntry(backupRunFullPath, entry.RelativePath, out var full))
            {
                issues.Add(new BackupVerificationIssue(entry.RelativePath, "unsafe-path", "Checkpoint path escapes the backup run."));
                continue;
            }

            expected[NormalizeRelative(entry.RelativePath)] = entry;
            if (!File.Exists(full))
            {
                issues.Add(new BackupVerificationIssue(entry.RelativePath, "missing", "Recorded file is missing."));
                continue;
            }

            checkedFiles++;
            try
            {
                var info = new FileInfo(full);
                if (info.Length != entry.SizeBytes)
                {
                    issues.Add(new BackupVerificationIssue(entry.RelativePath, "size", $"Expected {entry.SizeBytes} bytes; found {info.Length}."));
                    continue;
                }

                if (mode == BackupVerificationMode.Full
                    && !string.IsNullOrWhiteSpace(entry.ContentHashSha256)
                    && !string.Equals(ComputeFileHash(full), entry.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new BackupVerificationIssue(entry.RelativePath, "hash", "File content does not match its checkpoint."));
                }
            }
            catch (Exception ex)
            {
                issues.Add(new BackupVerificationIssue(entry.RelativePath, "unreadable", ex.Message));
            }
        }

        try
        {
            foreach (var file in EnumerateFilesStrict(backupRunFullPath))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(backupRunFullPath, file));
                if (!expected.ContainsKey(relative))
                {
                    issues.Add(new BackupVerificationIssue(relative, "extra", "File is not recorded in the checkpoint."));
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add(new BackupVerificationIssue(string.Empty, "enumeration", ex.Message));
        }

        return new BackupVerificationResult
        {
            BackupPath = backupRunFullPath,
            Mode = mode,
            CheckpointFound = true,
            ExpectedFiles = expected.Count,
            CheckedFiles = checkedFiles,
            Issues = issues,
        };
    }

    public static bool TryReadCheckpointCapturedAtUtc(string backupRunFullPath, out DateTime checkpointUtc)
    {
        checkpointUtc = default;
        try
        {
            if (!TryReadManifest(backupRunFullPath, out var doc)
                || string.IsNullOrWhiteSpace(doc.CheckpointCapturedAtUtc))
            {
                return false;
            }

            if (!DateTime.TryParse(doc.CheckpointCapturedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                return false;
            }

            checkpointUtc = parsed.ToUniversalTime();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeBackupRunPath(string path) =>
        Path.GetFullPath(path.Trim());

    private static bool PathsEqual(string a, string b) =>
        string.Equals(NormalizeBackupRunPath(a), NormalizeBackupRunPath(b), StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveManifestEntry(string runRoot, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var root = NormalizeBackupRunPath(runRoot);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!BackupPathSafety.PathsEqual(candidate, root)
                && !BackupPathSafety.IsContainedBy(candidate, root))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string ComputeFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IEnumerable<string> EnumerateFilesStrict(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Checkpoint tree contains a reparse point: {Path.GetRelativePath(root, entry)}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static bool IsStorageRootAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            if (root.StartsWith("\\\\", StringComparison.Ordinal))
            {
                return Directory.Exists(root);
            }

            return new DriveInfo(root).IsReady;
        }
        catch
        {
            return false;
        }
    }

    private static string Sha256HexLower(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private sealed class TestRootScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CheckpointsRootOverride.Value = previous;
        }
    }
}
