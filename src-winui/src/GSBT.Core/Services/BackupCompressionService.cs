using System.Diagnostics;
using SharpSevenZip;

namespace GSBT.Core.Services;

/// <summary>Compresses the backup folder to a single <c>.7z</c> archive via bundled <c>7z.dll</c>.</summary>
public sealed class BackupCompressionService
{
    /// <summary>
    /// Output archive is written <b>inside</b> <paramref name="backupFolder"/> as <c>Backups_yyyy-MM-dd_HH-mm-ss.7z</c>.
    /// Files collected for compression <b>exclude</b> prior GSBT full-folder archives at the backup root.
    /// </summary>
    public async Task<BackupCompressionResult> CompressBackupFolderAsync(
        string backupFolder,
        CompressionOptions options,
        IProgress<int>? progressPercent,
        Action<string>? log,
        Action<string>? reportActiveGameFolder = null,
        Action<CompressionGameTrackUpdate>? reportGameTrack = null,
        CancellationToken cancellationToken = default,
        bool subfolderPerGame = true,
        IReadOnlySet<string>? sanitizedGameFolderNames = null)
    {
        if (!SevenZipNativeLibrary.IsAvailable)
        {
            var err = SevenZipNativeLibrary.LastError ?? "7z.dll is not loaded.";
            return new BackupCompressionResult(
                false,
                $"Compression engine unavailable: {err}",
                string.Empty,
                0,
                0,
                0,
                options);
        }

        if (!Directory.Exists(backupFolder))
        {
            throw new DirectoryNotFoundException(backupFolder);
        }

        var (entries, totalBytes, fileCount) = CollectRelativeEntries(
            backupFolder,
            subfolderPerGame,
            sanitizedGameFolderNames);
        if (fileCount == 0)
        {
            return new BackupCompressionResult(true, "No files to compress.", string.Empty, 0, 0, 0, options);
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var archiveName = $"Backups_{stamp}.7z";
        var archivePath = Path.Combine(backupFolder, archiveName);

        void L(string m)
        {
            try
            {
                log?.Invoke(m);
            }
            catch
            {
                // ignore host logging failures
            }
        }

        L($"Compress ({options.SummaryLabel}) → {archiveName} …");
        var sw = Stopwatch.StartNew();
        try
        {
            await RunSevenZipNativeAsync(
                    archivePath,
                    options,
                    entries,
                    totalBytes,
                    fileCount,
                    progressPercent,
                    L,
                    reportActiveGameFolder,
                    reportGameTrack,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            TryDeletePartialArchive(archivePath);
            throw;
        }

        sw.Stop();
        var archSize = File.Exists(archivePath) ? new FileInfo(archivePath).Length : 0L;
        L($"Done in {sw.Elapsed.TotalSeconds:F1}s; archive {_humanBytes(archSize)} (raw input {_humanBytes(totalBytes)}).");
        return new BackupCompressionResult(
            true,
            $"Created {archiveName}",
            archivePath,
            totalBytes,
            archSize,
            sw.Elapsed.TotalSeconds,
            options);
    }

    private static void TryDeletePartialArchive(string archivePath)
    {
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch
        {
            // best-effort cleanup after cancel / failure
        }
    }

    private static Task RunSevenZipNativeAsync(
        string archivePath,
        CompressionOptions options,
        List<(string FullPath, string EntryName)> entries,
        long totalBytes,
        int fileCount,
        IProgress<int>? progressPercent,
        Action<string> log,
        Action<string>? reportActiveGameFolder,
        Action<CompressionGameTrackUpdate>? reportGameTrack,
        CancellationToken cancellationToken)
    {
        var fileDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bytesByEntry = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fullPath, entryName) in entries)
        {
            var key = entryName.Replace('\\', '/');
            fileDictionary[key] = fullPath;
            bytesByEntry[key] = TryGetFileLength(fullPath);
        }

        var gameOrder = GetOrderedGameFoldersFromEntries(entries);
        string? trackedPrevious = null;
        string? trackedCurrent = null;

        void ReportGameTransition(string gameFolder)
        {
            if (reportGameTrack is null || string.IsNullOrWhiteSpace(gameFolder))
            {
                return;
            }

            if (string.Equals(gameFolder, trackedCurrent, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.IsNullOrEmpty(trackedCurrent))
            {
                trackedPrevious = trackedCurrent;
            }

            trackedCurrent = gameFolder;
            var upcoming = string.Empty;
            for (var i = 0; i < gameOrder.Count; i++)
            {
                if (string.Equals(gameOrder[i], trackedCurrent, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < gameOrder.Count)
                {
                    upcoming = gameOrder[i + 1];
                    break;
                }
            }

            reportGameTrack(new CompressionGameTrackUpdate(
                trackedPrevious ?? string.Empty,
                trackedCurrent,
                upcoming));
        }

        return Task.Run(
            () =>
            {
                var mappedLevel = SevenZipCompressionLevelMapper.MapMxToCompressionLevel(options.SevenMx);
                var compressor = new SharpSevenZipCompressor
                {
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionMethod = CompressionMethod.Lzma2,
                    CompressionLevel = mappedLevel,
                    DirectoryStructure = true,
                    IncludeEmptyDirectories = false,
                };
                compressor.CustomParameters["mt"] = options.SevenMmt <= 0
                    ? "on"
                    : options.SevenMmt.ToString();
                compressor.CustomParameters["s"] = options.SolidArchive ? "on" : "off";

                var progress = new NativeCompressProgressTracker(totalBytes, fileCount);
                var lastLoggedPct = -1;

                void ReportProgress(int pct)
                {
                    pct = Math.Clamp(pct, 0, 99);
                    progressPercent?.Report(pct);
                    if (pct / 5 != lastLoggedPct / 5 || pct >= 95)
                    {
                        lastLoggedPct = pct;
                        log($"7-Zip… {pct}%");
                    }
                }

                compressor.Compressing += (_, e) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress(progress.ComputePercent(e.PercentDone));
                };

                compressor.FileCompressionStarted += (_, e) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        e.Cancel = true;
                        return;
                    }

                    var name = e.FileName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        return;
                    }

                    var key = name.Replace('\\', '/');
                    var fileBytes = bytesByEntry.TryGetValue(key, out var sz) ? sz : 0;
                    progress.OnFileStarted(fileBytes);
                    var gameFolder = TopLevelFolderFromEntry(key);
                    reportActiveGameFolder?.Invoke(gameFolder);
                    ReportGameTransition(gameFolder);
                };

                compressor.FileCompressionFinished += (_, _) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.OnFileFinished();
                    ReportProgress(progress.ComputePercent(0));
                };

                compressor.CompressFileDictionary(fileDictionary, archivePath, string.Empty);
                cancellationToken.ThrowIfCancellationRequested();
                progressPercent?.Report(100);
            },
            cancellationToken);
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Root-level archives created by this tool; must not be included in the next full-folder compress.</summary>
    internal static bool IsRootGsbtBackupArchiveRelativeEntry(string relativePathWithForwardSlashes)
    {
        var rel = relativePathWithForwardSlashes.Replace('\\', '/');
        if (rel.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (!rel.StartsWith("Backups_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return rel.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || rel.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
    }

    internal static (List<(string FullPath, string EntryName)> Entries, long TotalBytes, int Count) CollectRelativeEntries(
        string root,
        bool subfolderPerGame = true,
        IReadOnlySet<string>? sanitizedGameFolderNames = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var filter = sanitizedGameFolderNames is { Count: > 0 }
            ? new HashSet<string>(sanitizedGameFolderNames, StringComparer.OrdinalIgnoreCase)
            : null;

        var list = Directory.EnumerateFiles(
                root,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                })
            .Select(file =>
            {
                var rel = Path.GetRelativePath(root, file);
                var entry = rel.Replace(Path.DirectorySeparatorChar, '/');
                return (file, entry);
            })
            .Where(x => !IsRootGsbtBackupArchiveRelativeEntry(x.entry))
            .Where(x => filter is null || EntryMatchesGameFilter(x.entry, subfolderPerGame, filter))
            .OrderBy(x => x.entry, StringComparer.OrdinalIgnoreCase)
            .ToList();
        long total = 0;
        foreach (var (f, _) in list)
        {
            try
            {
                total += new FileInfo(f).Length;
            }
            catch
            {
                // ignore
            }
        }

        return (list, total, list.Count);
    }

    internal static IReadOnlyList<string> GetOrderedGameFoldersFromEntries(
        IReadOnlyList<(string FullPath, string EntryName)> entries)
    {
        var gameOrder = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, entryName) in entries)
        {
            var top = TopLevelFolderFromEntry(entryName.Replace('\\', '/'));
            if (string.IsNullOrEmpty(top) || !seen.Add(top))
            {
                continue;
            }

            gameOrder.Add(top);
        }

        return gameOrder;
    }

    internal static string TopLevelFolderFromEntry(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        var slash = normalized.IndexOf('/');
        return slash > 0 ? normalized[..slash] : normalized;
    }

    internal static bool EntryMatchesGameFilter(
        string relativeEntryWithForwardSlashes,
        bool subfolderPerGame,
        IReadOnlySet<string> sanitizedGameFolderNames)
    {
        var top = TopLevelFolderFromEntry(relativeEntryWithForwardSlashes);
        if (string.IsNullOrEmpty(top))
        {
            return false;
        }

        if (subfolderPerGame)
        {
            return sanitizedGameFolderNames.Contains(top);
        }

        foreach (var safe in sanitizedGameFolderNames)
        {
            if (top.StartsWith(safe + " - Backup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string _humanBytes(long num)
    {
        var n = (double)Math.Max(0, num);
        if (n >= 1024 * 1024 * 1024)
        {
            return $"{n / (1024 * 1024 * 1024):F2} GiB";
        }

        if (n >= 1024 * 1024)
        {
            return $"{n / (1024 * 1024):F2} MiB";
        }

        if (n >= 1024)
        {
            return $"{n / 1024:F2} KiB";
        }

        return $"{num} B";
    }
}

public sealed record BackupCompressionResult(
    bool Success,
    string Message,
    string ArchivePath,
    long RawBytes,
    long ArchiveBytes,
    double WallSeconds,
    CompressionOptions Options);
