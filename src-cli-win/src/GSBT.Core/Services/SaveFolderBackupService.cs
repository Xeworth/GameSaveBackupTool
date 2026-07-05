using GSBT.Core.Common;

namespace GSBT.Core.Services;

/// <summary>
/// Folder-tree backups aligned with Python <c>AutoBackupWorker</c> (copytree + retention by folder name prefix).
/// </summary>
public sealed class SaveFolderBackupService
{
    /// <summary>
    /// Makes a single path segment safe on Windows. Only strips <see cref="Path.GetInvalidFileNameChars"/> (no longer drops '#' etc.).
    /// </summary>
    public string SanitizeGameFolderName(string gameName) =>
        GameNameInputValidation.SanitizeForWindowsPathSegment(gameName);

    /// <summary>Performs a timestamped backup folder; prunes older runs for the same game.</summary>
    public void BackupToRetentionFolder(
        string gameName,
        string sourceSaveFolder,
        string backupRoot,
        int retentionCount,
        bool subfolderPerGame,
        CancellationToken cancellationToken,
        out string backupPath,
        out string? error)
    {
        backupPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(sourceSaveFolder) || !Directory.Exists(sourceSaveFolder))
        {
            error = "Save folder does not exist.";
            return;
        }

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
            var removed = PruneOldBackups(baseDir, safe, retentionCount);
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
        backupPath = Path.Combine(baseDir, $"{safe} - Backup {stamp}");
        try
        {
            CopyDirectory(sourceSaveFolder, backupPath, cancellationToken);
            BackupRunManifestStore.TryWriteManifest(gameName, sourceSaveFolder, backupPath);
        }
        catch (OperationCanceledException)
        {
            error = null;
            try
            {
                if (Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup after cancel
            }

            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            TryDeletePartialBackup(backupPath);
        }
    }

    private static void TryDeletePartialBackup(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return;
        }

        try
        {
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup after failure
        }
    }

    private static List<string> PruneOldBackups(string baseDir, string safeName, int retentionCount)
    {
        var removed = new List<string>();
        if (retentionCount <= 0 || !Directory.Exists(baseDir))
        {
            return removed;
        }

        var prefix = $"{safeName} - Backup";
        var dirs = Directory.EnumerateDirectories(baseDir)
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal))
            .Select(d => new FileInfo(d))
            .OrderBy(f => f.LastWriteTimeUtc)
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

    private static readonly EnumerationOptions CopyEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static void CopyDirectory(string sourceDir, string destDir, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destDir);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", CopyEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relDir = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relDir));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", CopyEnumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destDir, rel);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, overwrite: true);
        }
    }
}
