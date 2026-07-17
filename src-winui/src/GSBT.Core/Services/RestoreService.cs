using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using GSBT.Core.Common;
using GSBT.Core.Models;

namespace GSBT.Core.Services;

/// <summary>Plans and executes explicit, verified restores without changing the normal backup workflow.</summary>
public sealed class RestoreService
{
    private const int MaxRestoreFiles = 1_000_000;
    private const long MaxRestoreBytes = 8L * 1024 * 1024 * 1024 * 1024;

    public RestorePlan CreateFolderPlan(
        string gameName,
        string backupRunPath,
        string targetPath,
        RestoreMode mode)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var source = NormalizePath(backupRunPath, errors, "backup snapshot");
        var target = NormalizePath(targetPath, errors, "restore target");
        if (errors.Count > 0)
        {
            return InvalidPlan(gameName, backupRunPath, targetPath, mode, errors);
        }

        if (!Directory.Exists(source))
        {
            errors.Add("The selected backup snapshot is unavailable.");
        }

        if (BackupPathSafety.PathsEqual(source, target)
            || BackupPathSafety.IsContainedBy(target, source)
            || BackupPathSafety.IsContainedBy(source, target))
        {
            errors.Add("The restore target and backup snapshot must not contain one another.");
        }

        var verification = errors.Count == 0
            ? BackupRunManifestStore.Verify(source, BackupVerificationMode.Full)
            : null;
        if (verification is not null && !verification.IsValid)
        {
            errors.AddRange(verification.Issues.Select(issue =>
                $"{issue.Kind}: {issue.RelativePath} {issue.Message}".Trim()));
        }

        var files = new List<(string Relative, long Size)>();
        long totalBytes = 0;
        if (errors.Count == 0)
        {
            try
            {
                foreach (var file in EnumerateFilesStrict(source))
                {
                    var relative = Path.GetRelativePath(source, file);
                    if (!IsSafeRelativePath(relative))
                    {
                        errors.Add($"Unsafe backup entry: {relative}");
                        break;
                    }

                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        errors.Add($"Reparse-point backup entry cannot be restored automatically: {relative}");
                        break;
                    }

                    files.Add((relative, new FileInfo(file).Length));
                    checked
                    {
                        totalBytes += files[^1].Size;
                    }

                    if (files.Count > MaxRestoreFiles || totalBytes > MaxRestoreBytes)
                    {
                        errors.Add("Restore snapshot exceeds the safety limits.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Could not enumerate the snapshot: {ex.Message}");
            }
        }

        var conflicts = Directory.Exists(target)
            ? files.Count(file => File.Exists(Path.Combine(target, file.Relative)))
            : 0;
        if (!BackupPathSafety.HasSufficientFreeSpace(
                Path.GetDirectoryName(target) ?? target,
                totalBytes + (Directory.Exists(target) ? GetDirectorySize(target) : 0),
                out var capacityError))
        {
            errors.Add(capacityError ?? "Insufficient free space for restore staging.");
        }

        var running = FindLikelyRunningProcesses(gameName);
        if (running.Count > 0)
        {
            warnings.Add($"Close the game before restoring. Possible running process: {string.Join(", ", running)}.");
        }

        if (conflicts > 0)
        {
            warnings.Add($"{conflicts} existing file(s) will be replaced in the staged result.");
        }

        return new RestorePlan
        {
            GameName = gameName,
            BackupRunPath = source,
            TargetPath = target,
            Mode = mode,
            IsValid = errors.Count == 0,
            FileCount = files.Count,
            TotalBytes = totalBytes,
            ConflictCount = conflicts,
            RunningProcesses = running,
            Errors = errors,
            Warnings = warnings,
        };
    }

    public RestoreOperationResult ExecuteFolderRestore(
        RestorePlan plan,
        string safetyBackupRoot,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid || plan.IsRegistry)
        {
            return Failed(plan, "Restore plan is not valid for a folder restore.");
        }

        var source = BackupPathSafety.NormalizeDirectory(plan.BackupRunPath);
        var target = BackupPathSafety.NormalizeDirectory(plan.TargetPath);
        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return Failed(plan, "Restore target has no valid parent folder.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(parent, $".gsbt-restore-stage-{runId}");
        var rollback = Path.Combine(parent, $".gsbt-restore-rollback-{runId}");
        var journal = Path.Combine(parent, $".gsbt-restore-{runId}.json");
        var targetExisted = Directory.Exists(target);
        var targetMoved = false;

        try
        {
            using var operationLock = CrossProcessLock.Acquire("restore:" + target, TimeSpan.FromMinutes(10));
            Directory.CreateDirectory(parent);
            RecoverInterruptedRestores(parent, target);
            cancellationToken.ThrowIfCancellationRequested();

            if (plan.Mode == RestoreMode.Merge && targetExisted)
            {
                CopyTree(target, stage, overwrite: false, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(stage);
            }

            CopyTree(source, stage, overwrite: true, cancellationToken);
            VerifyExpectedFiles(source, stage, cancellationToken);
            WriteJournal(journal, target, stage, rollback, "staged");

            cancellationToken.ThrowIfCancellationRequested();
            if (targetExisted)
            {
                Directory.Move(target, rollback);
                targetMoved = true;
                WriteJournal(journal, target, stage, rollback, "target-moved");
            }

            Directory.Move(stage, target);
            WriteJournal(journal, target, stage, rollback, "promoted");
            VerifyExpectedFiles(source, target, cancellationToken);

            var safetyPath = string.Empty;
            var warnings = new List<string>();
            if (targetMoved && Directory.Exists(rollback))
            {
                safetyPath = BuildSafetyPath(safetyBackupRoot, plan.GameName, runId);
                try
                {
                    CopyTree(rollback, safetyPath, overwrite: false, cancellationToken);
                    if (!BackupRunManifestStore.TryWriteManifest(
                            plan.GameName + " (pre-restore)",
                            plan.TargetPath,
                            safetyPath,
                            out var checkpointError))
                    {
                        warnings.Add($"Safety snapshot was copied but not checkpointed: {checkpointError}");
                    }

                    Directory.Delete(rollback, recursive: true);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Original live save remains at '{rollback}' because its safety copy could not be finalized: {ex.Message}");
                    safetyPath = rollback;
                }
            }

            TryDeleteFile(journal);
            return new RestoreOperationResult
            {
                GameName = plan.GameName,
                Status = RestoreOperationStatus.Succeeded,
                TargetPath = target,
                SafetySnapshotPath = safetyPath,
                FilesRestored = plan.FileCount,
                BytesRestored = plan.TotalBytes,
                Warnings = warnings,
            };
        }
        catch (OperationCanceledException)
        {
            RollBackFolderSwap(target, stage, rollback, targetExisted, targetMoved);
            TryDeleteFile(journal);
            return new RestoreOperationResult
            {
                GameName = plan.GameName,
                Status = RestoreOperationStatus.Cancelled,
                TargetPath = target,
                Error = "Restore canceled; the original live save was preserved.",
            };
        }
        catch (Exception ex)
        {
            var rolledBack = RollBackFolderSwap(target, stage, rollback, targetExisted, targetMoved);
            TryDeleteFile(journal);
            return new RestoreOperationResult
            {
                GameName = plan.GameName,
                Status = rolledBack ? RestoreOperationStatus.RolledBack : RestoreOperationStatus.Failed,
                TargetPath = target,
                Error = rolledBack
                    ? $"Restore failed and the original live save was restored: {ex.Message}"
                    : $"Restore failed; manual recovery may be required: {ex.Message}",
            };
        }
    }

    [SupportedOSPlatform("windows")]
    public RestoreOperationResult ExecuteRegistryRestore(
        string gameName,
        string backupRunPath,
        string hive,
        string subkey,
        string safetyBackupRoot,
        CancellationToken cancellationToken)
    {
        var verification = BackupRunManifestStore.Verify(backupRunPath, BackupVerificationMode.Full);
        if (!verification.IsValid)
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
                Error = "Registry snapshot verification failed.",
                Warnings = verification.Issues.Select(static issue => issue.Message).ToList(),
            };
        }

        var regFiles = EnumerateFilesStrict(backupRunPath)
            .Where(path => path.EndsWith(".reg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (regFiles.Count != 1)
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
                Error = "A registry restore requires exactly one verified .reg file.",
            };
        }

        var expectedRegistryTarget = CanonicalRegistryTarget(hive, subkey);
        if (BackupRunManifestStore.TryReadManifest(backupRunPath, out var manifest)
            && manifest.IsRegistry
            && !string.Equals(manifest.SourceSaveDirectory, expectedRegistryTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = expectedRegistryTarget,
                Error = "The registry snapshot belongs to a different registry target.",
            };
        }

        if (!RegistryExportContainsOnlyTarget(regFiles[0], expectedRegistryTarget, out var registryValidationError))
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = expectedRegistryTarget,
                Error = registryValidationError,
            };
        }

        var safety = new RegistrySaveBackupService().BackupToRetentionFileWithResult(
            gameName + " Pre-Restore Safety",
            hive,
            subkey,
            safetyBackupRoot,
            10,
            true,
            cancellationToken);
        if (!safety.Success)
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Failed,
                TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
                Error = $"Could not create the required pre-restore registry snapshot: {safety.Error}",
            };
        }

        if (RunRegImport(regFiles[0], cancellationToken, out var importError))
        {
            return new RestoreOperationResult
            {
                GameName = gameName,
                Status = RestoreOperationStatus.Succeeded,
                TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
                SafetySnapshotPath = safety.BackupPath,
                FilesRestored = 1,
            };
        }

        var rollbackSucceeded = RunRegImport(safety.BackupPath, cancellationToken, out var rollbackError);
        return new RestoreOperationResult
        {
            GameName = gameName,
            Status = rollbackSucceeded ? RestoreOperationStatus.RolledBack : RestoreOperationStatus.Failed,
            TargetPath = RegistrySaveResolver.FormatRegistrySaveDisplay(hive, subkey),
            SafetySnapshotPath = safety.BackupPath,
            Error = rollbackSucceeded
                ? $"Registry restore failed and the prior registry state was restored: {importError}"
                : $"Registry restore and rollback both failed. Restore '{safety.BackupPath}' manually. Import: {importError}; rollback: {rollbackError}",
        };
    }

    public static IReadOnlyList<string> FindLikelyRunningProcesses(string gameName)
    {
        var meaningful = new string((gameName ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        if (meaningful.Length < 5)
        {
            return [];
        }

        try
        {
            return Process.GetProcesses()
                .Select(static process =>
                {
                    try
                    {
                        var name = process.ProcessName;
                        process.Dispose();
                        return name;
                    }
                    catch
                    {
                        process.Dispose();
                        return string.Empty;
                    }
                })
                .Where(name =>
                {
                    var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                    return normalized.Length >= 5
                        && (normalized.Contains(meaningful, StringComparison.Ordinal)
                            || meaningful.Contains(normalized, StringComparison.Ordinal));
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void CopyTree(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Reparse-point roots are not restored automatically.");
        }

        Directory.CreateDirectory(destination);
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, entry);
                if (!IsSafeRelativePath(relative))
                {
                    throw new IOException($"Unsafe restore entry: {relative}");
                }

                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Reparse-point restore entry is not allowed: {relative}");
                }

                var target = Path.Combine(destination, relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.CreateDirectory(target);
                    pending.Push(entry);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(entry, target, overwrite);
                }
            }
        }
    }

    private static void VerifyExpectedFiles(string source, string target, CancellationToken cancellationToken)
    {
        foreach (var sourceFile in EnumerateFilesStrict(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, sourceFile);
            var targetFile = Path.Combine(target, relative);
            if (!File.Exists(targetFile)
                || new FileInfo(sourceFile).Length != new FileInfo(targetFile).Length
                || !CryptographicOperations.FixedTimeEquals(
                    ComputeSha256(sourceFile),
                    ComputeSha256(targetFile)))
            {
                throw new IOException($"Restored file verification failed: {relative}");
            }
        }
    }

    private static byte[] ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        return SHA256.HashData(stream);
    }

    private static bool RegistryExportContainsOnlyTarget(
        string regFile,
        string expectedTarget,
        out string? error)
    {
        error = null;
        var sawTarget = false;
        try
        {
            foreach (var rawLine in File.ReadLines(regFile))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] != '[')
                {
                    continue;
                }

                var key = line.TrimStart('[').TrimStart('-').TrimEnd(']');
                if (!key.Equals(expectedTarget, StringComparison.OrdinalIgnoreCase)
                    && !key.StartsWith(expectedTarget + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Registry snapshot contains a key outside the requested target: {key}";
                    return false;
                }

                sawTarget = true;
            }
        }
        catch (Exception ex)
        {
            error = $"Registry snapshot could not be inspected: {ex.Message}";
            return false;
        }

        if (!sawTarget)
        {
            error = "Registry snapshot does not contain the requested registry target.";
            return false;
        }

        return true;
    }

    private static string CanonicalRegistryTarget(string hive, string subkey)
    {
        var canonicalHive = hive.Trim().ToUpperInvariant() switch
        {
            "HKCU" => "HKEY_CURRENT_USER",
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKU" => "HKEY_USERS",
            "HKCR" => "HKEY_CLASSES_ROOT",
            _ => hive.Trim().ToUpperInvariant(),
        };
        return RegistrySaveResolver.FormatRegistrySaveDisplay(
            canonicalHive,
            subkey.Trim().TrimStart('\\'));
    }

    private static bool RollBackFolderSwap(
        string target,
        string stage,
        string rollback,
        bool targetExisted,
        bool targetMoved)
    {
        try
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, recursive: true);
            }

            if (targetMoved && Directory.Exists(rollback))
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                Directory.Move(rollback, target);
            }
            else if (!targetExisted && Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            return !targetExisted || Directory.Exists(target);
        }
        catch
        {
            return false;
        }
    }

    private static void RecoverInterruptedRestores(string parent, string target)
    {
        foreach (var journalPath in Directory.EnumerateFiles(parent, ".gsbt-restore-*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(journalPath));
                var root = document.RootElement;
                var journalTarget = root.GetProperty("target").GetString();
                var rollback = root.GetProperty("rollback").GetString();
                var stage = root.GetProperty("stage").GetString();
                if (!BackupPathSafety.PathsEqual(journalTarget ?? string.Empty, target))
                {
                    continue;
                }

                if (!Directory.Exists(target) && !string.IsNullOrWhiteSpace(rollback) && Directory.Exists(rollback))
                {
                    Directory.Move(rollback, target);
                }

                if (!string.IsNullOrWhiteSpace(stage) && Directory.Exists(stage))
                {
                    Directory.Delete(stage, recursive: true);
                }

                TryDeleteFile(journalPath);
            }
            catch
            {
                // Leave unreadable journals for diagnostics; never guess at recovery paths.
            }
        }
    }

    private static void WriteJournal(string path, string target, string stage, string rollback, string state)
    {
        AtomicFileWrite.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            target,
            stage,
            rollback,
            state,
            updatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        }));
    }

    private static string BuildSafetyPath(string root, string gameName, string runId)
    {
        var safe = GameNameInputValidation.SanitizeForWindowsPathSegment(gameName);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_'at'_HH-mm-ss-fff");
        return Path.Combine(
            BackupPathSafety.NormalizeDirectory(root),
            safe,
            ".pre-restore",
            $"{safe} - Pre-Restore {stamp}-{runId[..8]}");
    }

    [SupportedOSPlatform("windows")]
    private static bool RunRegImport(string path, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        try
        {
            var start = new ProcessStartInfo("reg.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("import");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process is null)
            {
                error = "Could not start reg.exe.";
                return false;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort cancellation.
                }
            });
            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode == 0)
            {
                return true;
            }

            error = process.StandardError.ReadToEnd().Trim();
            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"reg import exited with code {process.ExitCode}.";
            }

            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string NormalizePath(string path, ICollection<string> errors, string label)
    {
        try
        {
            return BackupPathSafety.NormalizeDirectory(path);
        }
        catch (Exception ex)
        {
            errors.Add($"Invalid {label}: {ex.Message}");
            return path ?? string.Empty;
        }
    }

    private static bool IsSafeRelativePath(string relative) =>
        !string.IsNullOrWhiteSpace(relative)
        && !Path.IsPathRooted(relative)
        && !relative.Equals("..", StringComparison.Ordinal)
        && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static part => part == "..");

    private static long GetDirectorySize(string path)
    {
        try
        {
            long total = 0;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("Restore target contains a reparse point.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                    else
                    {
                        checked
                        {
                            total += new FileInfo(entry).Length;
                        }
                    }
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> EnumerateFilesStrict(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Restore tree contains a reparse point: {Path.GetRelativePath(root, entry)}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort journal cleanup.
        }
    }

    private static RestorePlan InvalidPlan(
        string gameName,
        string backupRunPath,
        string targetPath,
        RestoreMode mode,
        IReadOnlyList<string> errors) =>
        new()
        {
            GameName = gameName,
            BackupRunPath = backupRunPath,
            TargetPath = targetPath,
            Mode = mode,
            IsValid = false,
            Errors = errors,
        };

    private static RestoreOperationResult Failed(RestorePlan plan, string error) =>
        new()
        {
            GameName = plan.GameName,
            Status = RestoreOperationStatus.Failed,
            TargetPath = plan.TargetPath,
            Error = error,
        };
}
