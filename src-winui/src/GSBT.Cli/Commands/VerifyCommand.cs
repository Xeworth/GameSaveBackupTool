using System.Text.Json;
using GSBT.Cli.Catalog;
using GSBT.Cli.Output;
using GSBT.Core.Models;
using GSBT.Core.Selection;
using GSBT.Core.Services;

namespace GSBT.Cli.Commands;

public static class VerifyCommand
{
    public static int Run(
        CliHost host,
        IReadOnlyList<string> targets,
        BackupVerificationMode verificationMode,
        CliOutputMode mode,
        CancellationToken cancellationToken)
    {
        if (!mode.Json)
        {
            CliConsoleFormatter.WriteCommandStart("gsbt verify");
        }

        try
        {
            var snapshot = CatalogSnapshot.LoadCurrent(host);
            var resolution = GameTargetResolver.Resolve(
                snapshot.Entries,
                targets,
                GameTargetFilter.Compressible,
                defaultToAllEligible: targets.Count == 0);
            if (resolution.Resolved.Count == 0)
            {
                var error = resolution.Errors.Count > 0
                    ? string.Join(" ", resolution.Errors)
                    : "No backed-up games were found.";
                if (mode.Ai)
                {
                    CliAiContract.WriteError("verify", error, 1, "target_resolution");
                }
                else
                {
                    CliConsoleFormatter.WriteError(error);
                }

                return 1;
            }

            var root = host.Settings.ResolveBackupDestination();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                const string error = "The configured backup location is unavailable.";
                if (mode.Ai)
                {
                    CliAiContract.WriteError("verify", error, 1, "backup_unavailable");
                }
                else
                {
                    CliConsoleFormatter.WriteError(error);
                }

                return 1;
            }

            var subfolder = host.Settings.Get("backup_subfolder_per_game", true);
            var results = new List<object>();
            var failures = 0;
            for (var i = 0; i < resolution.Resolved.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var game = resolution.Resolved[i];
                CliProgressEvents.Write(
                    mode,
                    "verify",
                    "game",
                    $"Verifying {game.GameName}.",
                    i + 1,
                    resolution.Resolved.Count,
                    (int)Math.Round((i + 1) * 100d / resolution.Resolved.Count));
                var run = BackupRetentionVerifier.TryGetLatestRetentionRunDirectory(root, game.GameName, subfolder);
                BackupVerificationResult verification;
                if (string.IsNullOrWhiteSpace(run))
                {
                    verification = new BackupVerificationResult
                    {
                        BackupPath = string.Empty,
                        Mode = verificationMode,
                        CheckpointFound = false,
                        Issues = [new BackupVerificationIssue(string.Empty, "missing-run", "No retained backup run was found.")],
                    };
                }
                else
                {
                    verification = BackupRunManifestStore.Verify(run, verificationMode);
                }

                if (!verification.IsValid)
                {
                    failures++;
                }

                results.Add(new
                {
                    game = game.GameName,
                    valid = verification.IsValid,
                    checkpointFound = verification.CheckpointFound,
                    backupPath = verification.BackupPath,
                    mode = verification.Mode.ToString().ToLowerInvariant(),
                    expectedFiles = verification.ExpectedFiles,
                    checkedFiles = verification.CheckedFiles,
                    issues = verification.Issues.Select(issue => new
                    {
                        path = issue.RelativePath,
                        kind = issue.Kind,
                        message = issue.Message,
                    }),
                });

                if (!mode.Json)
                {
                    if (verification.IsValid)
                    {
                        Console.WriteLine($"  {game.GameName}: verified ({verification.CheckedFiles} files)");
                    }
                    else
                    {
                        CliConsoleFormatter.WriteWarning($"{game.GameName}: {verification.Issues.Count} verification issue(s)");
                        foreach (var issue in verification.Issues.Take(5))
                        {
                            Console.WriteLine($"    {issue.Kind}: {issue.RelativePath} {issue.Message}".TrimEnd());
                        }
                    }
                }
            }

            if (mode.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = CliAiContract.SchemaVersion,
                    command = "verify",
                    success = failures == 0,
                    version = GSBT.Core.Common.AppVersionInfo.DisplayVersion,
                    verificationMode = verificationMode.ToString().ToLowerInvariant(),
                    verifiedCount = results.Count - failures,
                    failedCount = failures,
                    results,
                }, CliAiContract.JsonOptions));
            }
            else
            {
                Console.WriteLine(failures == 0
                    ? $"Verified {results.Count} backup(s)."
                    : $"Verified {results.Count - failures}/{results.Count}; {failures} need attention.");
            }

            OperationHistoryStore.Record(
                "verify",
                failures == 0 ? "succeeded" : "issues-found",
                failures == 0 ? "All selected backups verified." : $"{failures} backup(s) need attention.",
                itemCount: results.Count);

            return failures == 0 ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            if (mode.Ai)
            {
                CliAiContract.WriteError("verify", "Verification canceled.", 130, "canceled");
            }

            return 130;
        }
        finally
        {
            if (!mode.Json)
            {
                CliConsoleFormatter.WriteCommandEnd();
            }
        }
    }
}
