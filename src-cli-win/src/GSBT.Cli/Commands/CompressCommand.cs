using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Core.Common;
using GSBT.Core.Selection;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class CompressCommand
{
    public const int ExitCanceled = 130;

    public static async Task<int> RunAsync(
        CliHost host,
        IReadOnlyList<string> targets,
        CliOutputMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt compress");
        }

        try
        {
        var backupRoot = host.Settings.ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            const string message = "No valid backup folder found. Set default_backup_path via gsbt settings backup-path or run gsbt backup.";
            if (mode.Ai)
            {
                CliAiContract.WriteError("compress", message, 1, "backup_destination");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 1;
        }

        host.EnsureSevenZip();
        if (!SevenZipNativeLibrary.IsAvailable)
        {
            var err = SevenZipNativeLibrary.LastError ?? "7z.dll is not loaded.";
            var message = $"Compression engine unavailable: {err}";
            if (mode.Ai)
            {
                CliAiContract.WriteError("compress", message, 2, "compression_engine");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 2;
        }

        var snapshot = CatalogSnapshot.LoadCurrent(host);
        if (snapshot.Entries.Count == 0)
        {
            const string message = "Catalog is empty. Run gsbt scan first.";
            if (mode.Ai)
            {
                CliAiContract.WriteError("compress", message, 1, "empty_catalog");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 1;
        }

        var resolution = GameTargetResolver.Resolve(
            snapshot.Entries,
            targets,
            GameTargetFilter.Compressible,
            defaultToAllEligible: targets.Count == 0);

        if (!mode.Json)
        {
            foreach (var warning in resolution.Warnings)
            {
                CliConsoleFormatter.WriteWarning(warning);
            }
        }

        if (resolution.HasErrors)
        {
            if (!mode.Json)
            {
                foreach (var err in resolution.Errors)
                {
                    CliConsoleFormatter.WriteError(err);
                }
            }

            if (resolution.Resolved.Count == 0)
            {
                if (mode.Ai)
                {
                    CliAiContract.WriteError(
                        "compress",
                        string.Join(" ", resolution.Errors),
                        1,
                        "target_resolution");
                }

                return 1;
            }
        }

        if (resolution.Resolved.Count == 0)
        {
            const string message = "No backup data found to compress. Run gsbt backup first.";
            if (mode.Ai)
            {
                CliAiContract.WriteError("compress", message, 1, "no_compressible_data");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 1;
        }

        var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
        HashSet<string>? sanitizedFilter = null;
        if (targets.Count > 0)
        {
            sanitizedFilter = resolution.Resolved
                .Select(g => GameNameInputValidation.SanitizeForWindowsPathSegment(g.GameName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var opts = CompressionOptionsResolver.FromSettings(
            host.Settings.Get,
            host.Settings.Get,
            host.Settings.Get);

        using var live = mode.ShowLive ? new CliLiveProgress(enabled: true) : null;
        var smoothMode = !opts.SolidArchive;
        var gameTotal = resolution.Resolved.Count;

        var progress = new Progress<int>(pct =>
        {
            live?.SetPercent(pct, smoothMode ? "Compressing (smooth)…" : "Compressing (chunky)…");
        });

        try
        {
            var result = await host.Compression.CompressBackupFolderAsync(
                backupRoot,
                opts,
                progress,
                log: _ => { },
                reportActiveGameFolder: null,
                reportGameTrack: null,
                cancellationToken: cancellationToken,
                subfolderPerGame: subfolder,
                sanitizedGameFolderNames: sanitizedFilter).ConfigureAwait(false);

            if (live is not null)
            {
                var branch = BuildCompressBranch(resolution.Resolved, result.Success, result.Message);
                var tail = result.Success ? result.Message : $"Failed: {result.Message}";
                live.CompleteWithBranch(branch, gameTotal, gameTotal, tail);
            }

            var run = new CompressRunResult
            {
                Success = result.Success,
                Message = result.Message,
                ArchivePath = result.ArchivePath,
                SelectedGames = resolution.Resolved.Select(r => r.GameName).ToList(),
            };
            CliConsoleFormatter.WriteCompressResult(run, mode.Json, branchRendered: live is not null);
            return result.Success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            live?.ClearLiveLine();
            if (mode.Json)
            {
                CliConsoleFormatter.WriteCompressCanceled(resolution.Resolved.Select(r => r.GameName).ToList());
            }
            else
            {
                CliConsoleFormatter.WriteWarning("Compression canceled.");
            }

            return ExitCanceled;
        }
        catch (Exception ex)
        {
            live?.ClearLiveLine();
            var message = $"Compress failed: {ex.Message}";
            if (mode.Ai)
            {
                CliAiContract.WriteError("compress", message, 2, "compress_failed");
            }
            else
            {
                CliConsoleFormatter.WriteError(message);
            }

            return 2;
        }
        }
        finally
        {
            if (!mode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }

    private static IReadOnlyList<CliBranchEntry> BuildCompressBranch(
        IReadOnlyList<Core.Models.CatalogGameEntry> games,
        bool success,
        string message)
    {
        if (success)
        {
            return games.Select(g => new CliBranchEntry(g.GameName, CliBranchStatus.Success, "compressed")).ToList();
        }

        var label = string.IsNullOrWhiteSpace(message) ? "not compressed" : message;
        return games.Select(g => new CliBranchEntry(g.GameName, CliBranchStatus.Error, label)).ToList();
    }
}
