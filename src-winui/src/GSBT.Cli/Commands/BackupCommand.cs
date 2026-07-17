using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Cli.Services;
using GSBT.Core.Selection;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class BackupCommand
{
    public const int ExitCanceled = 130;

    public static int Run(
        CliHost host,
        IReadOnlyList<string> targets,
        CliOutputMode mode,
        string? path,
        bool setDefault,
        bool acceptSuggestion,
        CancellationToken cancellationToken = default)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt backup");
        }

        try
        {
        if (!BackupDestinationResolver.TryResolve(
                host.Settings,
                new BackupDestinationResolver.Request(path, setDefault, acceptSuggestion, mode.NonInteractive),
                out var backupRoot,
                out _,
                out var destinationCanceled))
        {
            if (destinationCanceled)
            {
                if (!mode.Json)
                {
                    Console.WriteLine("Backup canceled. No files were changed.");
                }

                return ExitCanceled;
            }

            if (mode.Ai)
            {
                CliAiContract.WriteError(
                    "backup",
                    "Backup path required in --ai mode. Set gsbt settings backup-path, use --path, or --yes.",
                    1,
                    "backup_destination");
            }

            return 1;
        }

        var snapshot = CatalogSnapshot.LoadCurrent(host);
        if (snapshot.Entries.Count == 0)
        {
            var message = "Catalog is empty. Run gsbt scan first.";
            if (mode.Ai)
            {
                CliAiContract.WriteError("backup", message, 1, "empty_catalog");
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
            GameTargetFilter.Backupable,
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
                        "backup",
                        string.Join(" ", resolution.Errors),
                        1,
                        "target_resolution");
                }

                return 1;
            }
        }

        var guard = LargeSaveBackupGuard.FilterForBackup(
            resolution.Resolved,
            host.Settings,
            mode.NonInteractive);

        if (guard.Canceled)
        {
            if (!mode.Json)
            {
                Console.WriteLine("Backup canceled. No files were changed.");
            }

            return ExitCanceled;
        }

        var results = new List<BackupItemResult>();
        foreach (var skip in guard.Skipped)
        {
            results.Add(new BackupItemResult
            {
                GameName = skip.GameName,
                Success = false,
                Skipped = true,
                Error = skip.Reason,
            });

            if (!mode.Json)
            {
                CliConsoleFormatter.WriteWarning($"{skip.GameName}: {skip.Reason}");
            }
        }

        var toBackup = guard.Approved;
        var retention = Math.Max(1, host.Settings.Get("backup_retention_count", 3));
        var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
        var total = toBackup.Count;
        var index = 0;
        var canceled = false;

        using var progress = mode.ShowLive ? new CliLiveProgress(enabled: true) : null;
        CliProgressEvents.Write(mode, "backup", "start", $"Backing up {total} game(s).", current: 0, total: total, percent: 0);

        foreach (var entry in toBackup)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            index++;
            CliProgressEvents.Write(
                mode,
                "backup",
                "game",
                $"Backing up {entry.GameName}.",
                current: index,
                total: total,
                percent: total <= 0 ? null : (int)Math.Clamp((index - 1) * 100.0 / total, 0, 100));
            progress?.SetCounter(index, total, $"Backing up {entry.GameName}…");

            if (!entry.IsBackupable)
            {
                results.Add(new BackupItemResult
                {
                    GameName = entry.GameName,
                    Success = false,
                    Error = entry.BackupSkipReason ?? "Not backupable.",
                    Skipped = true,
                });
                continue;
            }

            GSBT.Core.Models.BackupOperationResult operation;
            try
            {
                if (entry.SaveInRegistryOnly)
                {
                    operation = host.RegistryBackup.BackupToRetentionFileWithResult(
                        entry.GameName,
                        entry.SaveRegistryHive!,
                        entry.SaveRegistrySubkey!,
                        backupRoot,
                        retention,
                        subfolder,
                        cancellationToken);
                }
                else
                {
                    operation = host.FolderBackup.BackupToRetentionFolderWithResult(
                        entry.GameName,
                        entry.SavePathResolved!,
                        backupRoot,
                        retention,
                        subfolder,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                operation = new GSBT.Core.Models.BackupOperationResult
                {
                    GameName = entry.GameName,
                    Status = GSBT.Core.Models.BackupOperationStatus.Failed,
                    Error = ex.Message,
                };
            }

            if (operation.Success)
            {
                var nowIso = DateTime.UtcNow.ToString("O");
                host.CatalogManager.UpdateLastBackup(entry.GameName, nowIso);
                results.Add(new BackupItemResult
                {
                    GameName = entry.GameName,
                    Success = true,
                    BackupPath = operation.BackupPath,
                    RunId = operation.RunId,
                    FilesCopied = operation.FilesCopied,
                    BytesCopied = operation.BytesCopied,
                    Warnings = operation.Warnings,
                });
                CliProgressEvents.Write(
                    mode,
                    "backup",
                    "game-complete",
                    $"{entry.GameName} backed up.",
                    current: index,
                    total: total,
                    percent: total <= 0 ? null : (int)Math.Clamp(index * 100.0 / total, 0, 100));
            }
            else
            {
                results.Add(new BackupItemResult
                {
                    GameName = entry.GameName,
                    Success = false,
                    BackupPath = operation.BackupPath,
                    Error = operation.Error,
                    RunId = operation.RunId,
                    FilesCopied = operation.FilesCopied,
                    BytesCopied = operation.BytesCopied,
                    Warnings = operation.Warnings,
                });
                CliProgressEvents.Write(mode, "backup", "game-failed", $"{entry.GameName}: {operation.Error}", current: index, total: total);
            }

            OperationHistoryStore.Record(
                "backup",
                operation.Success ? "succeeded" : operation.Status.ToString().ToLowerInvariant(),
                operation.Success ? "Backup completed." : operation.Error ?? "Backup failed.",
                entry.GameName,
                operation.BackupPath,
                operation.BytesCopied,
                operation.FilesCopied);
        }

        host.CatalogManager.Flush();
        if (!canceled || results.Any(r => r.Success))
        {
            BackupDestinationResolver.RecordLastBackupPath(host.Settings, backupRoot);
        }

        if (progress is not null)
        {
            var branch = results.Select(ToBranchEntry).ToList();
            var attempted = resolution.Resolved.Count;
            var tail = BuildBackupSummary(results, attempted, canceled);
            progress.CompleteWithBranch(branch, attempted, attempted, tail);
        }

        CliConsoleFormatter.WriteBackupResults(results, mode.Json, branchRendered: progress is not null, canceled: canceled);
        if (canceled)
        {
            return ExitCanceled;
        }

        return results.Count > 0 && results.All(r => r.Success) ? 0 : 1;
        }
        finally
        {
            if (!mode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }

    private static CliBranchEntry ToBranchEntry(BackupItemResult r)
    {
        if (r.Success)
        {
            return new CliBranchEntry(r.GameName, CliBranchStatus.Success);
        }

        if (r.Skipped)
        {
            var message = string.IsNullOrWhiteSpace(r.Error) ? "not backed up" : r.Error;
            return new CliBranchEntry(r.GameName, CliBranchStatus.Warning, message);
        }

        var errMessage = string.IsNullOrWhiteSpace(r.Error) ? "failed" : r.Error;
        return new CliBranchEntry(r.GameName, CliBranchStatus.Error, errMessage);
    }

    private static string BuildBackupSummary(IReadOnlyList<BackupItemResult> results, int attempted, bool canceled)
    {
        var succeeded = results.Count(r => r.Success);
        var partial = results.Count(r => !r.Success && r.Skipped);
        var failed = results.Count(r => !r.Success && !r.Skipped);

        if (canceled)
        {
            var message = $"Backup canceled. {succeeded}/{attempted} backed up successfully.";
            return succeeded == attempted ? message : CliLiveProgress.WarningText(message);
        }

        if (attempted > 0 && succeeded == attempted && partial == 0 && failed == 0)
        {
            return "Backup complete.";
        }

        var parts = new List<string>
        {
            $"{succeeded}/{attempted} backed up successfully",
        };

        if (partial > 0)
        {
            parts.Add(CliLiveProgress.WarningText($"{partial}/{attempted} partial"));
        }

        if (failed > 0)
        {
            parts.Add(CliLiveProgress.ErrorText($"{failed}/{attempted} failed"));
        }

        var summary = string.Join(", ", parts) + ".";
        var issue = FirstBackupIssue(results);
        if (!string.IsNullOrWhiteSpace(issue))
        {
            summary += " " + (failed > 0
                ? CliLiveProgress.ErrorText(issue)
                : CliLiveProgress.WarningText(issue));
        }

        return summary;
    }

    private static string? FirstBackupIssue(IReadOnlyList<BackupItemResult> results)
    {
        var first = results.FirstOrDefault(r => !r.Success && !string.IsNullOrWhiteSpace(r.Error));
        return first is null
            ? null
            : $"{first.GameName}: {first.Error}";
    }
}
