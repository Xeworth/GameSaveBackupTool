using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace GSBT.Core.Services;

/// <summary>Compresses the backup folder to a single archive beside Python behavior (zipfile or 7-Zip).</summary>
public sealed class BackupCompressionService
{
    /// <summary>
    /// Output archive is written <b>inside</b> <paramref name="backupFolder"/> as <c>Backups_yyyy-MM-dd_HH-mm-ss.{ext}</c>.
    /// Files collected for compression <b>exclude</b> prior GSBT full-folder archives at the backup root (<c>Backups_*.zip</c> / <c>Backups_*.7z</c>)
    /// so a new archive does not nest older archives.
    /// </summary>
    public async Task<BackupCompressionResult> CompressBackupFolderAsync(
        string backupFolder,
        CompressionOptions options,
        IProgress<int>? progressPercent,
        Action<string>? log,
        Action<string>? reportActiveGameFolder = null,
        CancellationToken cancellationToken = default,
        bool subfolderPerGame = true,
        IReadOnlySet<string>? sanitizedGameFolderNames = null)
    {
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
        var ext = options.Engine == "7z"
            ? (options.SevenArchiveFormat is "zip" or "7z" ? options.SevenArchiveFormat : "7z")
            : "zip";
        var archiveName = $"Backups_{stamp}.{ext}";
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
            if (options.Engine == "7z")
            {
                if (string.IsNullOrEmpty(options.SevenZipExe) || !File.Exists(options.SevenZipExe))
                {
                    return new BackupCompressionResult(false, "7-Zip executable not found. Install 7-Zip or set path in Settings → Compression.", string.Empty, 0, 0, 0, options);
                }

                await RunSevenZipAsync(
                        backupFolder,
                        archivePath,
                        options,
                        entries,
                        totalBytes,
                        progressPercent,
                        L,
                        reportActiveGameFolder,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await RunZipArchiveAsync(
                        backupFolder,
                        archivePath,
                        options,
                        entries,
                        fileCount,
                        progressPercent,
                        L,
                        reportActiveGameFolder,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
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

    private static async Task RunZipArchiveAsync(
        string backupFolder,
        string archivePath,
        CompressionOptions options,
        List<(string FullPath, string EntryName)> entries,
        int fileCount,
        IProgress<int>? progressPercent,
        Action<string> log,
        Action<string>? reportActiveGameFolder,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        var level = MapCompressionLevel(options);
        var count = 0;
        foreach (var (full, entryName) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportActiveGameFolder?.Invoke(TopLevelFolderFromEntry(entryName));
            var entry = archive.CreateEntry(entryName, level);
            await using var entryStream = entry.Open();
            await using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await input.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            count++;
            var pct = fileCount > 0 ? (int)(100.0 * count / fileCount) : 100;
            progressPercent?.Report(Math.Min(100, pct));
            if (count % 25 == 0 || count == fileCount)
            {
                log($"ZIP… {count}/{fileCount} files");
            }
        }

        progressPercent?.Report(100);
    }

    private static CompressionLevel MapCompressionLevel(CompressionOptions options)
    {
        if (options.ZipKind == CompressionKind.Stored)
        {
            return CompressionLevel.NoCompression;
        }

        var d = Math.Clamp(options.DeflateLevel, 1, 9);
        return d switch
        {
            >= 8 => CompressionLevel.SmallestSize,
            <= 3 => CompressionLevel.Fastest,
            _ => CompressionLevel.Optimal,
        };
    }

    private static async Task RunSevenZipAsync(
        string backupFolder,
        string archivePath,
        CompressionOptions options,
        List<(string FullPath, string EntryName)> entries,
        long totalBytes,
        IProgress<int>? progressPercent,
        Action<string> log,
        Action<string>? reportActiveGameFolder,
        CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(Path.GetTempPath(), $"gsbt_7z_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllLinesAsync(
                    listPath,
                    entries.Select(e => e.EntryName.Replace('/', Path.DirectorySeparatorChar)),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);

            var outAbs = Path.GetFullPath(archivePath);
            var listAbs = Path.GetFullPath(listPath);
            var fmt = options.SevenArchiveFormat is "zip" or "7z" ? options.SevenArchiveFormat : "7z";
            var args = new List<string>();
            if (fmt == "7z")
            {
                args.AddRange(new[] { "a", "-t7z", "-m0=lzma2", $"-mx={options.SevenMx}" });
            }
            else
            {
                args.AddRange(new[] { "a", "-tzip", $"-mx={options.SevenMx}" });
            }

            if (options.SevenMmt <= 0)
            {
                args.Add("-mmt=on");
            }
            else
            {
                args.Add($"-mmt={options.SevenMmt}");
            }

            args.AddRange(new[] { "-bso0", "-y", outAbs, $"@{listAbs}" });

            var psi = new ProcessStartInfo
            {
                FileName = options.SevenZipExe!,
                WorkingDirectory = backupFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                throw new InvalidOperationException("Could not start 7-Zip process.");
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
            var t0 = DateTime.UtcNow;
            var floor = 0;
            var fileCount = entries.Count;
            var lastReportedEntryIndex = -1;
            while (!proc.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                var zsz = File.Exists(outAbs) ? new FileInfo(outAbs).Length : 0L;
                var pct = EstimateSevenZipUiPercent(zsz, totalBytes, t0);
                pct = Math.Max(floor, Math.Min(95, pct));
                floor = pct;
                progressPercent?.Report(pct);
                if (fileCount > 0)
                {
                    var idx = (int)Math.Round(pct / 100.0 * Math.Max(0, fileCount - 1));
                    idx = Math.Clamp(idx, 0, fileCount - 1);
                    ReportSevenZipEntryProgress(
                        lastReportedEntryIndex,
                        idx,
                        entries,
                        reportActiveGameFolder,
                        out lastReportedEntryIndex);
                }

                log($"7-Zip… ~{pct}% (~{zsz / (1024 * 1024)} MiB on disk)");
            }

            if (fileCount > 0)
            {
                ReportSevenZipEntryProgress(
                    lastReportedEntryIndex,
                    fileCount - 1,
                    entries,
                    reportActiveGameFolder,
                    out _);
            }

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"7-Zip failed with exit code {proc.ExitCode}.");
            }

            progressPercent?.Report(100);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(listPath))
                {
                    File.Delete(listPath);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>Rough progress from growing archive size (Python <c>_seven_zip_ui_percent</c> simplified).</summary>
    private static int EstimateSevenZipUiPercent(long archiveBytes, long totalUncompressed, DateTime startUtc)
    {
        if (totalUncompressed <= 0)
        {
            return 0;
        }

        var elapsed = (DateTime.UtcNow - startUtc).TotalSeconds;
        var totalMb = Math.Max(1e-9, totalUncompressed / (1024.0 * 1024.0));
        var wallGuess = Math.Max(6.0, Math.Min(90.0, 4.5 + Math.Pow(totalMb, 0.5) * 2.4 + totalMb * 0.05));
        var estSec = Math.Max(3.2, Math.Min(36.0, wallGuess * 0.42));
        var ratio = 1.0 - Math.Exp(-elapsed / estSec);
        var timePct = (int)(94 * Math.Pow(ratio, 0.48));
        var sizePct = (int)(100.0 * archiveBytes / totalUncompressed);
        var blended = Math.Max(sizePct, timePct);
        var scaled = (int)(blended * 1.115 + 1.5);
        return Math.Clamp(scaled, 0, 95);
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

    /// <summary>
    /// Reports each top-level game folder from <paramref name="lastReportedEntryIndex"/> + 1 through
    /// <paramref name="estimatedEntryIndex"/> so fast/small games are not skipped when 7-Zip progress jumps.
    /// </summary>
    internal static void ReportSevenZipEntryProgress(
        int lastReportedEntryIndex,
        int estimatedEntryIndex,
        IReadOnlyList<(string FullPath, string EntryName)> entries,
        Action<string>? reportActiveGameFolder,
        out int newLastReportedEntryIndex)
    {
        newLastReportedEntryIndex = lastReportedEntryIndex;
        if (entries.Count == 0 || reportActiveGameFolder is null)
        {
            return;
        }

        var idx = Math.Clamp(estimatedEntryIndex, 0, entries.Count - 1);
        for (var i = lastReportedEntryIndex + 1; i <= idx; i++)
        {
            reportActiveGameFolder(TopLevelFolderFromEntry(entries[i].EntryName));
        }

        newLastReportedEntryIndex = Math.Max(lastReportedEntryIndex, idx);
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
