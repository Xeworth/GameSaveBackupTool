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
                out _))
        {
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

        foreach (var entry in toBackup)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            index++;
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

            string? err;
            string backupPath;
            try
            {
                if (entry.SaveInRegistryOnly)
                {
                    host.RegistryBackup.BackupToRetentionFile(
                        entry.GameName,
                        entry.SaveRegistryHive!,
                        entry.SaveRegistrySubkey!,
                        backupRoot,
                        retention,
                        subfolder,
                        CancellationToken.None,
                        out backupPath,
                        out err);
                }
                else
                {
                    host.FolderBackup.BackupToRetentionFolder(
                        entry.GameName,
                        entry.SavePathResolved!,
                        backupRoot,
                        retention,
                        subfolder,
                        CancellationToken.None,
                        out backupPath,
                        out err);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                backupPath = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(err))
            {
                var nowIso = DateTime.UtcNow.ToString("O");
                host.CatalogManager.UpdateLastBackup(entry.GameName, nowIso);
                results.Add(new BackupItemResult
                {
                    GameName = entry.GameName,
                    Success = true,
                    BackupPath = backupPath,
                });
            }
            else
            {
                results.Add(new BackupItemResult
                {
                    GameName = entry.GameName,
                    Success = false,
                    Error = err,
                });
            }
        }

        host.CatalogManager.Flush();
        if (!canceled || results.Any(r => r.Success))
        {
            BackupDestinationResolver.RecordLastBackupPath(host.Settings, backupRoot);
        }

        if (progress is not null)
        {
            var ok = results.Count(r => r.Success);
            var branch = results.Select(ToBranchEntry).ToList();
            var attempted = resolution.Resolved.Count;
            var tail = canceled
                ? $"Backup canceled. ({ok}/{attempted} completed)"
                : ok == attempted && attempted > 0 && guard.Skipped.Count == 0
                    ? "Backup complete."
                    : $"Backup finished: {ok}/{attempted} succeeded.";
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
            return new CliBranchEntry(r.GameName, CliBranchStatus.Success, "done");
        }

        if (r.Skipped)
        {
            var label = string.IsNullOrWhiteSpace(r.Error) ? "not backed up" : r.Error;
            return new CliBranchEntry(r.GameName, CliBranchStatus.Warning, label);
        }

        var errLabel = string.IsNullOrWhiteSpace(r.Error) ? "failed" : r.Error;
        return new CliBranchEntry(r.GameName, CliBranchStatus.Error, errLabel);
    }
}
