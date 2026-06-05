using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>
/// Persists per–backup-run checkpoints under <c>%AppData%\GSBT\backup_run_checkpoints\</c> (hidden from the backup folder itself).
/// </summary>
public static class BackupRunManifestStore
{
    private const string CheckpointsSubDir = "backup_run_checkpoints";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string GetCheckpointsRootDirectory() =>
        Path.Combine(UserDataDir.GetAppUserDataDir(), CheckpointsSubDir);

    public static string GetStoragePathForBackupRun(string backupRunFullPath)
    {
        var normalized = NormalizeBackupRunPath(backupRunFullPath);
        var hash = Sha256HexLower(normalized);
        return Path.Combine(GetCheckpointsRootDirectory(), $"{hash}.json");
    }

    /// <summary>Best-effort write after a successful folder backup. Does not throw to callers.</summary>
    public static void TryWriteManifest(string gameName, string sourceSaveDirectory, string backupRunDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(backupRunDirectory) || !Directory.Exists(backupRunDirectory))
            {
                return;
            }

            Directory.CreateDirectory(GetCheckpointsRootDirectory());
            var runNorm = NormalizeBackupRunPath(backupRunDirectory);
            var manifest = new BackupRunCheckpointManifest
            {
                WriterVersion = typeof(BackupRunManifestStore).Assembly.GetName().Version?.ToString(),
                GameName = gameName ?? string.Empty,
                BackupRunDirectory = runNorm,
                SourceSaveDirectory = string.IsNullOrWhiteSpace(sourceSaveDirectory)
                    ? string.Empty
                    : NormalizeBackupRunPath(sourceSaveDirectory),
                CheckpointCapturedAtUtc = DateTime.UtcNow.ToString("O")
            };

            foreach (var file in Directory.EnumerateFiles(backupRunDirectory, "*", SearchOption.AllDirectories))
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
                        LastWriteTimeUtc = fi.LastWriteTimeUtc.ToString("O")
                    });
                }
                catch
                {
                    // skip unreadable file
                }
            }

            var dest = GetStoragePathForBackupRun(backupRunDirectory);
            var tmp = dest + ".tmp";
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(tmp, json);
            File.Move(tmp, dest, overwrite: true);
        }
        catch
        {
            // non-fatal
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

                if (!Directory.Exists(doc.BackupRunDirectory))
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

    /// <summary>True if a checkpoint exists and any recorded file is missing or its size differs under <paramref name="backupRunFullPath"/>.</summary>
    public static bool HasManifestDrift(string backupRunFullPath)
    {
        try
        {
            if (!Directory.Exists(backupRunFullPath))
            {
                return false;
            }

            var path = GetStoragePathForBackupRun(backupRunFullPath);
            if (!File.Exists(path))
            {
                return false;
            }

            var text = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<BackupRunCheckpointManifest>(text, JsonOptions);
            if (doc?.Files is null || doc.Files.Count == 0)
            {
                return false;
            }

            var runNorm = NormalizeBackupRunPath(backupRunFullPath);
            if (!PathsEqual(doc.BackupRunDirectory, runNorm))
            {
                return false;
            }

            foreach (var entry in doc.Files)
            {
                if (string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    return true;
                }

                var full = Path.Combine(backupRunFullPath, entry.RelativePath);
                if (!File.Exists(full))
                {
                    return true;
                }

                try
                {
                    if (new FileInfo(full).Length != entry.SizeBytes)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadCheckpointCapturedAtUtc(string backupRunFullPath, out DateTime checkpointUtc)
    {
        checkpointUtc = default;
        try
        {
            var path = GetStoragePathForBackupRun(backupRunFullPath);
            if (!File.Exists(path))
            {
                return false;
            }

            var text = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<BackupRunCheckpointManifest>(text, JsonOptions);
            if (doc is null || string.IsNullOrWhiteSpace(doc.CheckpointCapturedAtUtc))
            {
                return false;
            }

            var runNorm = NormalizeBackupRunPath(backupRunFullPath);
            if (!PathsEqual(doc.BackupRunDirectory, runNorm))
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
}
