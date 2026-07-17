namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<RestoreSnapshotOption> GetRestoreSnapshots(GameRowViewModel row)
    {
        var root = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        return BackupRetentionVerifier.ListRetentionRunDirectories(root, row.GameName, subfolder)
            .Select(path =>
            {
                BackupRunManifestStore.TryReadManifest(path, out var manifest);
                var captured = manifest is not null
                    && DateTimeOffset.TryParse(manifest.CheckpointCapturedAtUtc, out var timestamp)
                        ? BackupDateFormatter.FormatDisplay(timestamp.ToString("O"), _settings.Get("date_format", BackupDateFormatter.DefaultFormatKey))
                        : Path.GetFileName(path);
                return new RestoreSnapshotOption(path, captured, manifest?.RunId ?? string.Empty);
            })
            .ToList();
    }

    public async Task<BackupVerificationResult?> VerifyLatestBackupAsync(GameRowViewModel row)
    {
        var snapshot = GetRestoreSnapshots(row).FirstOrDefault();
        if (snapshot is null)
        {
            return null;
        }

        BeginCancellableOperation(FooterCancelSlot.None);
        StatusText = $"Verifying {row.GameName}...";
        try
        {
            var token = _operationCts!.Token;
            var result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return BackupRunManifestStore.Verify(snapshot.Path, BackupVerificationMode.Full);
            }, token).ConfigureAwait(true);
            var status = result.IsValid ? "succeeded" : "failed";
            var message = result.IsValid
                ? $"Verified {result.CheckedFiles:N0} file(s)."
                : result.Issues.FirstOrDefault()?.Message ?? "Backup verification failed.";
            StatusText = result.IsValid
                ? $"Verified {row.GameName} at {BackupDateFormatter.FormatDisplay(DateTime.UtcNow.ToString("O"), _settings.Get("date_format", BackupDateFormatter.DefaultFormatKey))}."
                : $"Verification failed: {message}";
            OperationHistoryStore.Record("verify", status, message, row.GameName, result.BackupPath);
            return result;
        }
        finally
        {
            EndCancellableOperation();
            ScanProgress = 0;
        }
    }

    public RestorePlan CreateRestorePlan(
        GameRowViewModel row,
        RestoreSnapshotOption snapshot,
        RestoreMode mode,
        string? alternateTarget)
    {
        if (row.SaveInRegistryOnly)
        {
            var verification = BackupRunManifestStore.Verify(snapshot.Path, BackupVerificationMode.Full);
            return new RestorePlan
            {
                GameName = row.GameName,
                BackupRunPath = snapshot.Path,
                TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(
                    row.SaveRegistryHive ?? string.Empty,
                    row.SaveRegistrySubkey ?? string.Empty),
                Mode = RestoreMode.Replace,
                IsRegistry = true,
                IsValid = verification.IsValid,
                FileCount = verification.ExpectedFiles,
                Errors = verification.Issues.Select(issue => issue.Message).ToList(),
                Warnings = ["Registry restore changes Windows registry data and creates a safety export first."],
            };
        }

        var target = mode == RestoreMode.Alternate ? alternateTarget : row.SavePathResolved;
        if (string.IsNullOrWhiteSpace(target))
        {
            return new RestorePlan
            {
                GameName = row.GameName,
                BackupRunPath = snapshot.Path,
                TargetPath = string.Empty,
                Mode = mode,
                IsValid = false,
                Errors = ["Choose a valid restore target folder."],
            };
        }

        return new RestoreService().CreateFolderPlan(row.GameName, snapshot.Path, target, mode);
    }

    public async Task<RestoreOperationResult> ExecuteRestoreAsync(GameRowViewModel row, RestorePlan plan)
    {
        var root = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(root))
        {
            return new RestoreOperationResult
            {
                GameName = row.GameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = plan.TargetPath,
                Error = "The configured backup location is unavailable.",
            };
        }

        BeginCancellableOperation(FooterCancelSlot.None);
        StatusText = $"Restoring {row.GameName}...";
        try
        {
            var token = _operationCts!.Token;
            var result = await Task.Run(() =>
            {
                var service = new RestoreService();
                if (plan.IsRegistry)
                {
                    return service.ExecuteRegistryRestore(
                        row.GameName,
                        plan.BackupRunPath,
                        row.SaveRegistryHive!,
                        row.SaveRegistrySubkey!,
                        root,
                        token);
                }

                return service.ExecuteFolderRestore(plan, root, token);
            }, token).ConfigureAwait(true);

            StatusText = result.Success
                ? $"Restore complete: {row.GameName}."
                : result.Error ?? "Restore failed.";
            OperationHistoryStore.Record(
                "restore",
                result.Status.ToString().ToLowerInvariant(),
                result.Success ? "Restore completed." : result.Error ?? "Restore failed.",
                row.GameName,
                result.TargetPath,
                result.BytesRestored,
                result.FilesRestored);
            return result;
        }
        finally
        {
            EndCancellableOperation();
            ScanProgress = 0;
        }
    }
}
