using System.Text.Json;
using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Cli.Input;
using GSBT.Core.Models;
using GSBT.Core.Selection;
using GSBT.Core.Services;
using Spectre.Console;

namespace GSBT.Cli.Commands;

public static class RestoreCommand
{
    private const int UnsafeTargetExitCode = 3;
    private const int InsufficientSpaceExitCode = 4;
    private const int VerificationFailureExitCode = 5;
    private const int RolledBackExitCode = 6;
    private const int RollbackFailureExitCode = 7;
    private const int PartialRestoreExitCode = 8;

    public static int Run(
        CliHost host,
        IReadOnlyList<string> targetTokens,
        string snapshotToken,
        string? alternateTarget,
        string modeToken,
        bool dryRun,
        bool confirmed,
        CliOutputMode outputMode,
        CancellationToken cancellationToken)
    {
        if (!outputMode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt restore");
        }

        try
        {
            var snapshot = CatalogSnapshot.LoadCurrent(host);
            var resolution = GameTargetResolver.Resolve(
                snapshot.Entries,
                targetTokens,
                GameTargetFilter.Compressible,
                defaultToAllEligible: false);
            if (resolution.Resolved.Count != 1)
            {
                var error = resolution.Resolved.Count > 1
                    ? "Restore accepts one game at a time. Use a full name or row number."
                    : string.Join(" ", resolution.Errors);
                WriteError(outputMode, error, "target_resolution");
                return 1;
            }

            var game = resolution.Resolved[0];
            var backupRoot = host.Settings.ResolveBackupDestination();
            if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
            {
                WriteError(outputMode, "The configured backup location is unavailable.", "backup_unavailable");
                return 1;
            }

            var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
            var backupRun = ResolveSnapshot(backupRoot, game.GameName, subfolder, snapshotToken);
            if (string.IsNullOrWhiteSpace(backupRun))
            {
                WriteError(outputMode, $"No snapshot matches '{snapshotToken}'.", "snapshot_not_found");
                return 1;
            }

            var restoreMode = ParseMode(modeToken, alternateTarget);
            var isRegistry = game.SaveInRegistryOnly;
            RestorePlan plan;
            if (isRegistry)
            {
                var verification = BackupRunManifestStore.Verify(backupRun, BackupVerificationMode.Full);
                plan = new RestorePlan
                {
                    GameName = game.GameName,
                    BackupRunPath = backupRun,
                    TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(
                        game.SaveRegistryHive ?? string.Empty,
                        game.SaveRegistrySubkey ?? string.Empty),
                    Mode = RestoreMode.Replace,
                    IsRegistry = true,
                    IsValid = verification.IsValid,
                    FileCount = verification.ExpectedFiles,
                    TotalBytes = BackupRunManifestStore.TryReadManifest(backupRun, out var manifest)
                        ? manifest.Files.Sum(file => file.SizeBytes)
                        : 0,
                    Errors = verification.Issues.Select(issue => issue.Message).ToList(),
                    Warnings = ["Registry restore changes Windows registry data and requires explicit confirmation."],
                };
            }
            else
            {
                var target = string.IsNullOrWhiteSpace(alternateTarget)
                    ? game.SavePathResolved
                    : alternateTarget;
                if (string.IsNullOrWhiteSpace(target))
                {
                    WriteError(outputMode, "The game has no live save folder. Use --to <folder>.", "restore_target_missing");
                    return 1;
                }

                plan = new RestoreService().CreateFolderPlan(game.GameName, backupRun, target, restoreMode);
            }

            WritePlan(outputMode, plan, dryRun);
            if (!plan.IsValid)
            {
                return ClassifyInvalidPlanExitCode(plan);
            }

            if (dryRun)
            {
                return 0;
            }

            if (!confirmed)
            {
                if (outputMode.NonInteractive)
                {
                    WriteError(outputMode, "Restore requires --yes in non-interactive or --ai mode.", "confirmation_required");
                    return 1;
                }

                var confirmation = CliPrompt.Confirm("Proceed with this restore?", defaultValue: false);
                if (confirmation != CliConfirmation.Accepted)
                {
                    Console.WriteLine("Restore canceled. No files were changed.");
                    return 130;
                }
            }

            CliProgressEvents.Write(outputMode, "restore", "start", $"Restoring {game.GameName}.", percent: 0);
            var service = new RestoreService();
            RestoreOperationResult result;
            if (isRegistry)
            {
                result = service.ExecuteRegistryRestore(
                    game.GameName,
                    backupRun,
                    game.SaveRegistryHive!,
                    game.SaveRegistrySubkey!,
                    backupRoot,
                    cancellationToken);
            }
            else
            {
                result = service.ExecuteFolderRestore(plan, backupRoot, cancellationToken);
            }

            CliProgressEvents.Write(
                outputMode,
                "restore",
                result.Success ? "complete" : "failed",
                result.Success ? "Restore complete." : result.Error ?? "Restore failed.",
                percent: result.Success ? 100 : null);
            WriteResult(outputMode, plan, result);
            OperationHistoryStore.Record(
                "restore",
                result.Status.ToString().ToLowerInvariant(),
                result.Success ? "Restore completed." : result.Error ?? "Restore failed.",
                game.GameName,
                result.TargetPath,
                result.BytesRestored,
                result.FilesRestored);
            return result.Status switch
            {
                RestoreOperationStatus.Succeeded => 0,
                RestoreOperationStatus.Cancelled => 130,
                RestoreOperationStatus.RolledBack => RolledBackExitCode,
                RestoreOperationStatus.Partial => PartialRestoreExitCode,
                _ => RollbackFailureExitCode,
            };
        }
        catch (OperationCanceledException)
        {
            WriteError(outputMode, "Restore canceled.", "canceled", 130);
            return 130;
        }
        catch (Exception ex)
        {
            WriteError(outputMode, ex.Message, "restore_failed", 2);
            return 2;
        }
        finally
        {
            if (!outputMode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }

    private static int ClassifyInvalidPlanExitCode(RestorePlan plan)
    {
        if (plan.Errors.Any(error => error.Contains("space", StringComparison.OrdinalIgnoreCase)))
        {
            return InsufficientSpaceExitCode;
        }

        if (plan.Errors.Any(error =>
                error.Contains("unsafe", StringComparison.OrdinalIgnoreCase)
                || error.Contains("must not contain", StringComparison.OrdinalIgnoreCase)
                || error.Contains("reparse", StringComparison.OrdinalIgnoreCase)))
        {
            return UnsafeTargetExitCode;
        }

        if (plan.Errors.Any(error =>
                error.Contains("hash", StringComparison.OrdinalIgnoreCase)
                || error.Contains("checkpoint", StringComparison.OrdinalIgnoreCase)
                || error.Contains("missing", StringComparison.OrdinalIgnoreCase)
                || error.Contains("changed", StringComparison.OrdinalIgnoreCase)
                || error.Contains("extra", StringComparison.OrdinalIgnoreCase)))
        {
            return VerificationFailureExitCode;
        }

        return 1;
    }

    private static string? ResolveSnapshot(string root, string gameName, bool subfolder, string token)
    {
        var runs = BackupRetentionVerifier.ListRetentionRunDirectories(root, gameName, subfolder);
        if (runs.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(token) || token.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return runs[0];
        }

        if (Path.IsPathRooted(token))
        {
            var candidate = BackupPathSafety.NormalizeDirectory(token);
            return BackupPathSafety.IsContainedBy(candidate, root)
                && runs.Any(run => BackupPathSafety.PathsEqual(run, candidate))
                    ? candidate
                    : null;
        }

        foreach (var run in runs)
        {
            if (BackupRunManifestStore.TryReadManifest(run, out var manifest)
                && manifest.RunId.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return run;
            }

            if (Path.GetFileName(run).Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return run;
            }
        }

        return null;
    }

    private static RestoreMode ParseMode(string token, string? alternateTarget)
    {
        if (!string.IsNullOrWhiteSpace(alternateTarget))
        {
            return RestoreMode.Alternate;
        }

        return token.Trim().ToLowerInvariant() switch
        {
            "merge" => RestoreMode.Merge,
            _ => RestoreMode.Replace,
        };
    }

    private static void WritePlan(CliOutputMode mode, RestorePlan plan, bool dryRun)
    {
        if (mode.Json)
        {
            if (dryRun)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = CliAiContract.SchemaVersion,
                    command = "restore",
                    success = plan.IsValid,
                    dryRun = true,
                    plan = ToMachinePlan(plan),
                }, CliAiContract.JsonOptions));
            }

            return;
        }

        Console.WriteLine($"  Game       : {plan.GameName}");
        Console.WriteLine($"  Snapshot   : {plan.BackupRunPath}");
        Console.WriteLine($"  Target     : {plan.TargetPath}");
        Console.WriteLine($"  Mode       : {plan.Mode}");
        Console.WriteLine($"  Files      : {plan.FileCount}");
        Console.WriteLine($"  Conflicts  : {plan.ConflictCount}");
        foreach (var warning in plan.Warnings)
        {
            CliConsoleFormatter.WriteWarning(warning);
        }

        foreach (var error in plan.Errors)
        {
            CliConsoleFormatter.WriteError(error);
        }
    }

    private static void WriteResult(CliOutputMode mode, RestorePlan plan, RestoreOperationResult result)
    {
        if (mode.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = CliAiContract.SchemaVersion,
                command = "restore",
                success = result.Success,
                dryRun = false,
                plan = ToMachinePlan(plan),
                result = ToMachineResult(result),
            }, CliAiContract.JsonOptions));
            return;
        }

        if (result.Success)
        {
            Console.WriteLine($"Restored {result.FilesRestored} file(s) to {result.TargetPath}.");
            if (!string.IsNullOrWhiteSpace(result.SafetySnapshotPath))
            {
                Console.WriteLine($"Pre-restore safety snapshot: {result.SafetySnapshotPath}");
            }
        }
        else
        {
            CliConsoleFormatter.WriteError(result.Error ?? "Restore failed.");
        }

        foreach (var warning in result.Warnings)
        {
            CliConsoleFormatter.WriteWarning(warning);
        }
    }

    private static object ToMachinePlan(RestorePlan plan) => new
    {
        gameName = plan.GameName,
        backupRunPath = plan.BackupRunPath,
        targetPath = plan.TargetPath,
        mode = plan.Mode.ToString().ToLowerInvariant(),
        isRegistry = plan.IsRegistry,
        isValid = plan.IsValid,
        fileCount = plan.FileCount,
        totalBytes = plan.TotalBytes,
        conflictCount = plan.ConflictCount,
        runningProcesses = plan.RunningProcesses,
        errors = plan.Errors,
        warnings = plan.Warnings,
    };

    private static object ToMachineResult(RestoreOperationResult result) => new
    {
        gameName = result.GameName,
        status = result.Status.ToString().ToLowerInvariant(),
        targetPath = result.TargetPath,
        safetySnapshotPath = result.SafetySnapshotPath,
        filesRestored = result.FilesRestored,
        bytesRestored = result.BytesRestored,
        error = result.Error,
        warnings = result.Warnings,
        success = result.Success,
    };

    private static void WriteError(CliOutputMode mode, string message, string code, int exitCode = 1)
    {
        if (mode.Ai)
        {
            CliAiContract.WriteError("restore", message, exitCode, code);
        }
        else
        {
            CliConsoleFormatter.WriteError(message);
        }
    }
}
