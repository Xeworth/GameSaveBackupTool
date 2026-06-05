using GSBT.Core.Common;

namespace GSBT.Core.Services;

/// <summary>
/// Checks whether GSBT retention backup folders still exist under the configured default backup root
/// (same layout as <see cref="SaveFolderBackupService"/>).
/// </summary>
public static class BackupRetentionVerifier
{
    /// <returns>True if at least one non-empty retention run exists: a directory matching <c>{sanitizedName} - Backup *</c> containing at least one file (recursive).</returns>
    public static bool HasRetentionArtifact(string backupRoot, string gameName, bool subfolderPerGame)
    {
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            return false;
        }

        var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(
            string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);

        var baseDir = subfolderPerGame
            ? Path.Combine(backupRoot, safe)
            : backupRoot;

        if (!Directory.Exists(baseDir))
        {
            return false;
        }

        if (HasRetentionRunDirectory(baseDir, safe) || HasLegacyFlatRegExport(baseDir, safe))
        {
            return true;
        }

        return false;
    }

    /// <summary>Infers the newest retention backup time (prefers AppData checkpoint <c>checkpointCapturedAtUtc</c> when present).</summary>
    public static string? TryInferLatestLastBackupIso(
        string backupRoot,
        string gameName,
        bool subfolderPerGame)
    {
        var latest = TryGetLatestRetentionRunDirectory(backupRoot, gameName, subfolderPerGame);
        DateTime bestUtc = DateTime.MinValue;
        if (!string.IsNullOrWhiteSpace(latest))
        {
            bestUtc = EffectiveUtcForRetentionRun(latest);
        }

        var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(
            string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);
        var baseDir = subfolderPerGame
            ? Path.Combine(backupRoot, safe)
            : backupRoot;

        if (Directory.Exists(baseDir))
        {
            var legacyUtc = TryGetLatestLegacyFlatRegUtc(baseDir, safe);
            if (legacyUtc is { } lu && lu > bestUtc)
            {
                bestUtc = lu;
            }
        }

        if (bestUtc == DateTime.MinValue)
        {
            return null;
        }

        return bestUtc.ToString("O");
    }

    /// <summary>Full path of the newest non-empty retention run for <paramref name="gameName"/> (by effective checkpoint/write time).</summary>
    public static string? TryGetLatestRetentionRunDirectory(string backupRoot, string gameName, bool subfolderPerGame)
    {
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            return null;
        }

        var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(
            string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);

        var baseDir = subfolderPerGame
            ? Path.Combine(backupRoot, safe)
            : backupRoot;

        if (!Directory.Exists(baseDir))
        {
            return null;
        }

        string? best = null;
        var bestEff = DateTime.MinValue;
        foreach (var dir in EnumerateNonEmptyRetentionRunDirectories(baseDir, safe))
        {
            var eff = EffectiveUtcForRetentionRun(dir);
            if (eff > bestEff)
            {
                bestEff = eff;
                best = dir;
            }
        }

        return best;
    }

    private static DateTime EffectiveUtcForRetentionRun(string runDir)
    {
        if (BackupRunManifestStore.TryReadCheckpointCapturedAtUtc(runDir, out var cp))
        {
            return cp;
        }

        try
        {
            return Directory.GetLastWriteTimeUtc(runDir);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Sum of on-disk file lengths for every non-empty retention run folder for <paramref name="gameName"/>.</summary>
    public static long ComputeTotalRetentionBackupBytes(string backupRoot, string gameName, bool subfolderPerGame)
    {
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            return 0;
        }

        var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(
            string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName);

        var baseDir = subfolderPerGame
            ? Path.Combine(backupRoot, safe)
            : backupRoot;

        if (!Directory.Exists(baseDir))
        {
            return 0;
        }

        long sum = 0;
        foreach (var dir in EnumerateNonEmptyRetentionRunDirectories(baseDir, safe))
        {
            sum += BackupFolderSizeEstimator.ComputeDirectoryLogicalSize(dir);
        }

        foreach (var file in EnumerateLegacyFlatRegExports(baseDir, safe))
        {
            try
            {
                sum += new FileInfo(file).Length;
            }
            catch
            {
                // ignore unreadable files
            }
        }

        return sum;
    }

    private static bool HasRetentionRunDirectory(string baseDir, string safe)
    {
        foreach (var dir in EnumerateNonEmptyRetentionRunDirectories(baseDir, safe))
        {
            return true;
        }

        return false;
    }

    private static bool HasLegacyFlatRegExport(string baseDir, string safe)
    {
        foreach (var _ in EnumerateLegacyFlatRegExports(baseDir, safe))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateNonEmptyRetentionRunDirectories(string baseDir, string safe)
    {
        var prefix = $"{safe} - Backup";
        foreach (var dir in Directory.EnumerateDirectories(baseDir))
        {
            if (!Path.GetFileName(dir).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (RetentionRunContainsAtLeastOneFile(dir))
            {
                yield return dir;
            }
        }
    }

    private static IEnumerable<string> EnumerateLegacyFlatRegExports(string baseDir, string safe)
    {
        if (!Directory.Exists(baseDir))
        {
            yield break;
        }

        var prefix = $"{safe} - Backup";
        foreach (var file in Directory.EnumerateFiles(baseDir, $"{prefix}*.reg"))
        {
            if (Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static DateTime? TryGetLatestLegacyFlatRegUtc(string baseDir, string safe)
    {
        DateTime? best = null;
        foreach (var file in EnumerateLegacyFlatRegExports(baseDir, safe))
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(file);
                if (best is null || t > best.Value)
                {
                    best = t;
                }
            }
            catch
            {
                // ignore
            }
        }

        return best;
    }

    /// <summary>Empty retention folders (name matches but copy failed or user recreated shell dirs) do not count as backups.</summary>
    public static bool RetentionRunContainsAtLeastOneFile(string retentionRunDirectory)
    {
        if (string.IsNullOrWhiteSpace(retentionRunDirectory) || !Directory.Exists(retentionRunDirectory))
        {
            return false;
        }

        try
        {
            using var e = Directory.EnumerateFiles(retentionRunDirectory, "*", SearchOption.AllDirectories).GetEnumerator();
            return e.MoveNext();
        }
        catch
        {
            return false;
        }
    }
}
