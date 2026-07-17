using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>Transactional folder-tree backups with checkpoint verification and post-success retention.</summary>
public sealed class SaveFolderBackupService
{
    private sealed record CopyFile(string SourcePath, string RelativePath, long SizeBytes);

    private sealed record CopyPlan(IReadOnlyList<string> Directories, IReadOnlyList<CopyFile> Files, long TotalBytes);

    public string SanitizeGameFolderName(string gameName) =>
        GameNameInputValidation.SanitizeForWindowsPathSegment(gameName);

    /// <summary>Compatibility wrapper for existing GUI/CLI callers.</summary>
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
        var result = BackupToRetentionFolderWithResult(
            gameName,
            sourceSaveFolder,
            backupRoot,
            retentionCount,
            subfolderPerGame,
            cancellationToken);

        backupPath = result.BackupPath;
        error = result.Success
            ? null
            : result.Error ?? "Backup did not complete.";
    }

    public BackupOperationResult BackupToRetentionFolderWithResult(
        string gameName,
        string sourceSaveFolder,
        string backupRoot,
        int retentionCount,
        bool subfolderPerGame,
        CancellationToken cancellationToken)
    {
        var displayName = string.IsNullOrWhiteSpace(gameName) ? "Game" : gameName.Trim();
        var sourceForResult = sourceSaveFolder ?? string.Empty;
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            return Failed(displayName, sourceForResult, "Backup destination is not set.");
        }

        if (!BackupPathSafety.TryValidateSourceAndDestination(
                sourceForResult,
                backupRoot,
                out var source,
                out var root,
                out var pathError))
        {
            return Failed(displayName, sourceForResult, pathError ?? "Unsafe backup path.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        CopyPlan plan;
        try
        {
            plan = BuildCopyPlan(source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(displayName, source, $"Could not read the complete save folder: {ex.Message}");
        }

        if (!BackupPathSafety.HasSufficientFreeSpace(root, plan.TotalBytes, out var capacityError))
        {
            return Failed(displayName, source, capacityError ?? "Insufficient destination space.");
        }

        var safe = SanitizeGameFolderName(displayName);
        var baseDir = subfolderPerGame ? Path.Combine(root, safe) : root;
        var runId = Guid.NewGuid().ToString("N");
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_'at'_HH-mm-ss-fff");
        var finalPath = Path.Combine(baseDir, $"{safe} - Backup {stamp}-{runId[..8]}");
        var stagingPath = Path.Combine(baseDir, $".gsbt-staging-{runId}");

        try
        {
            Directory.CreateDirectory(root);
            using var rootLease = OperationFileLease.Acquire(
                Path.Combine(root, ".gsbt-operation.lock"),
                TimeSpan.FromMinutes(10),
                cancellationToken);
            using var operationLock = CrossProcessLock.Acquire($"backup:{baseDir}:{displayName}", TimeSpan.FromMinutes(10));
            Directory.CreateDirectory(baseDir);
            CleanStaleStagingDirectories(baseDir);
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(stagingPath);
            CopyPlanToStaging(plan, stagingPath, cancellationToken);
            VerifyStaging(plan, stagingPath, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingPath, finalPath);

            if (!BackupRunManifestStore.TryWriteManifest(displayName, source, finalPath, out var manifestError))
            {
                return new BackupOperationResult
                {
                    GameName = displayName,
                    Status = BackupOperationStatus.Partial,
                    Source = source,
                    BackupPath = finalPath,
                    RunId = runId,
                    FilesCopied = plan.Files.Count,
                    BytesCopied = plan.TotalBytes,
                    Error = $"Backup files were copied, but verification metadata could not be finalized: {manifestError}",
                };
            }

            var warnings = PruneOldBackupsAfterSuccess(baseDir, safe, displayName, Math.Max(1, retentionCount));
            return new BackupOperationResult
            {
                GameName = displayName,
                Status = BackupOperationStatus.Succeeded,
                Source = source,
                BackupPath = finalPath,
                RunId = runId,
                FilesCopied = plan.Files.Count,
                BytesCopied = plan.TotalBytes,
                Warnings = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteStaging(stagingPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteStaging(stagingPath);
            return Failed(displayName, source, ex.Message, finalPath, runId, plan.Files.Count, plan.TotalBytes);
        }
    }

    private static CopyPlan BuildCopyPlan(string source, CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The save folder is a reparse point. Select its real target folder explicitly.");
        }

        var directories = new List<string>();
        var files = new List<CopyFile>();
        long totalBytes = 0;
        var pending = new Stack<string>();
        pending.Push(source);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                var relative = Path.GetRelativePath(source, entry);
                if (Path.IsPathRooted(relative)
                    || relative.Equals("..", StringComparison.Ordinal)
                    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new IOException($"A save entry escapes the selected folder: {entry}");
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Reparse point is not backed up automatically: {relative}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relative);
                    pending.Push(entry);
                    continue;
                }

                var size = new FileInfo(entry).Length;
                checked
                {
                    totalBytes += size;
                }

                files.Add(new CopyFile(entry, relative, size));
            }
        }

        return new CopyPlan(directories, files, totalBytes);
    }

    private static void CopyPlanToStaging(CopyPlan plan, string stagingPath, CancellationToken cancellationToken)
    {
        foreach (var relative in plan.Directories.OrderBy(static p => p.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(stagingPath, relative));
        }

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(stagingPath, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.SourcePath, destination, overwrite: false);
        }
    }

    private static void VerifyStaging(CopyPlan plan, string stagingPath, CancellationToken cancellationToken)
    {
        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(stagingPath, file.RelativePath);
            if (!File.Exists(destination))
            {
                throw new IOException($"Copied file is missing during verification: {file.RelativePath}");
            }

            var actual = new FileInfo(destination).Length;
            if (actual != file.SizeBytes)
            {
                throw new IOException($"Copied file size changed during verification: {file.RelativePath}");
            }
        }

        var actualCount = Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories).Count();
        if (actualCount != plan.Files.Count)
        {
            throw new IOException($"Backup verification expected {plan.Files.Count} files but found {actualCount}.");
        }
    }

    private static IReadOnlyList<string> PruneOldBackupsAfterSuccess(
        string baseDir,
        string safeName,
        string gameName,
        int retentionCount)
    {
        var warnings = new List<string>();
        var prefix = $"{safeName} - Backup";
        var allMatching = Directory.EnumerateDirectories(baseDir)
            .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(d => new
            {
                Path = d,
                Manifest = BackupRunManifestStore.TryReadManifest(d, out var manifest) ? manifest : null,
            })
            .ToList();

        var hasDifferentOwner = allMatching.Any(x =>
            x.Manifest is not null
            && !string.Equals(x.Manifest.GameName, gameName, StringComparison.OrdinalIgnoreCase));

        var owned = allMatching
            .Where(x => x.Manifest is not null
                ? string.Equals(x.Manifest.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                : !hasDifferentOwner)
            .OrderBy(x => TryGetEffectiveUtc(x.Path))
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
                warnings.Add($"Could not prune old backup '{Path.GetFileName(oldest)}': {ex.Message}");
                break;
            }
        }

        return warnings;
    }

    private static DateTime TryGetEffectiveUtc(string runPath)
    {
        if (BackupRunManifestStore.TryReadCheckpointCapturedAtUtc(runPath, out var captured))
        {
            return captured;
        }

        try
        {
            return Directory.GetLastWriteTimeUtc(runPath);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void CleanStaleStagingDirectories(string baseDir)
    {
        foreach (var path in Directory.EnumerateDirectories(baseDir, ".gsbt-staging-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // A live or inaccessible staging folder is left for a later cleanup.
            }
        }
    }

    private static void TryDeleteStaging(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path)
                && Path.GetFileName(path).StartsWith(".gsbt-staging-", StringComparison.Ordinal)
                && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // The uniquely named staging path cannot replace or prune a prior valid backup.
        }
    }

    private static BackupOperationResult Failed(
        string gameName,
        string source,
        string error,
        string backupPath = "",
        string runId = "",
        int filesCopied = 0,
        long bytesCopied = 0) =>
        new()
        {
            GameName = gameName,
            Status = BackupOperationStatus.Failed,
            Source = source,
            BackupPath = backupPath,
            RunId = runId,
            FilesCopied = filesCopied,
            BytesCopied = bytesCopied,
            Error = error,
        };
}
