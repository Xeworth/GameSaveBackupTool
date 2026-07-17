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
        CliProgressEvents.Write(mode, "compress", "start", $"Compressing {gameTotal} game(s).", current: 0, total: gameTotal, percent: 0);

        var aiProgress = mode.Ai ? new CliProgressEventThrottle() : null;
        _ = aiProgress?.Observe(0);
        var aiProgressGate = new object();
        var progressState = new CompressionProgressState();
        var modeName = smoothMode ? "smooth" : "chunky";

        void ObserveAiProgress(int percent)
        {
            if (aiProgress is null)
            {
                return;
            }

            lock (aiProgressGate)
            {
                if (aiProgress.Observe(percent) is not { } emission)
                {
                    return;
                }

                var message = emission.IsHeartbeat
                    ? emission.Percent >= 99
                        ? $"Still finalizing the archive ({modeName})."
                        : $"Still compressing ({modeName})."
                    : $"Compressing ({modeName}).";
                var firstHeartbeat = CliAgentNotebook.ShouldIncludeLiveHint(
                    emission.IsHeartbeat,
                    emission.PlateauSeconds);
                CliProgressEvents.Write(
                    mode,
                    "compress",
                    "compress",
                    message,
                    percent: emission.Percent,
                    heartbeat: emission.IsHeartbeat,
                    elapsedSeconds: emission.ElapsedSeconds,
                    plateauSeconds: emission.IsHeartbeat ? emission.PlateauSeconds : null,
                    compressionMode: modeName,
                    compressionLevel: opts.SevenMx,
                    agentStatus: emission.IsHeartbeat ? "working" : null,
                    knowledgeRef: emission.IsHeartbeat
                        ? CliAgentNotebook.GetKnowledgeId(emission.Percent, chunky: !smoothMode)
                        : null,
                    agentHint: firstHeartbeat
                        ? CliAgentNotebook.GetLiveHint(emission.Percent, chunky: !smoothMode)
                        : null);
            }
        }

        var progress = new InlineProgress<int>(pct =>
        {
            progressState.Percent = pct;
            ObserveAiProgress(pct);

            live?.SetPercent(pct, smoothMode ? "Compressing (smooth)…" : "Compressing (chunky)…");
        });
        using var heartbeatCancellation = mode.Ai
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        var heartbeatTask = heartbeatCancellation is null
            ? Task.CompletedTask
            : ObserveCompressionHeartbeatAsync(
                progressState,
                ObserveAiProgress,
                heartbeatCancellation.Token);

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
                var tail = result.Success
                    ? result.Message
                    : CliLiveProgress.ErrorText($"Compression failed: {result.Message}");
                live.CompleteWithBranch(branch, gameTotal, gameTotal, tail);
            }

            var run = new CompressRunResult
            {
                Success = result.Success,
                Message = result.Message,
                ArchivePath = result.ArchivePath,
                SelectedGames = resolution.Resolved.Select(r => r.GameName).ToList(),
                CompressionMode = smoothMode ? "smooth" : "chunky",
                CompressionLevel = opts.SevenMx,
                CompressionThreads = opts.SevenMmt <= 0 ? "auto" : opts.SevenMmt.ToString(),
                SolidArchive = opts.SolidArchive,
                ElapsedSeconds = result.WallSeconds,
                InputBytes = result.RawBytes,
                ArchiveBytes = result.ArchiveBytes,
            };
            CliProgressEvents.Write(mode, "compress", result.Success ? "complete" : "failed", result.Message, percent: result.Success ? 100 : null);
            OperationHistoryStore.Record(
                "compress",
                result.Success ? "succeeded" : "failed",
                result.Message,
                outputPath: result.ArchivePath,
                bytes: result.ArchiveBytes,
                itemCount: resolution.Resolved.Count);
            CliConsoleFormatter.WriteCompressResult(run, mode.Json, branchRendered: live is not null);
            return result.Success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            OperationHistoryStore.Record("compress", "cancelled", "Compression canceled.");
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
            OperationHistoryStore.Record("compress", "failed", ex.Message);
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
        finally
        {
            if (heartbeatCancellation is not null)
            {
                await heartbeatCancellation.CancelAsync().ConfigureAwait(false);
                await heartbeatTask.ConfigureAwait(false);
            }
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

    private static async Task ObserveCompressionHeartbeatAsync(
        CompressionProgressState progressState,
        Action<int> observeProgress,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                observeProgress(progressState.Percent);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed class CompressionProgressState
    {
        public volatile int Percent;
    }

    private static IReadOnlyList<CliBranchEntry> BuildCompressBranch(
        IReadOnlyList<Core.Models.CatalogGameEntry> games,
        bool success,
        string message)
    {
        if (success)
        {
            return games.Select(g => new CliBranchEntry(g.GameName, CliBranchStatus.Success)).ToList();
        }

        var detail = string.IsNullOrWhiteSpace(message) ? "not compressed" : message;
        return games.Select(g => new CliBranchEntry(g.GameName, CliBranchStatus.Error, detail)).ToList();
    }
}
