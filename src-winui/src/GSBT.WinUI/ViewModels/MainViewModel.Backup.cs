namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    public bool BackupSizeEstimateEnabled => _settings.Get("backup_size_estimate_enabled", true);

    public bool WarnBackupFolderNameCollisionsEnabled =>
        _settings.Get("warn_backup_folder_name_collisions", true);

    public IReadOnlyList<GameRowViewModel> GetBackupCandidates(IReadOnlyList<GameRowViewModel> selected) =>
        selected.Count > 0
            ? selected.Where(CanBackupRow).ToList()
            : Games.Where(CanBackupRow).ToList();

    public IReadOnlyList<string> GetSanitizedFolderCollisionWarnings(IReadOnlyList<GameRowViewModel> candidates) =>
        GameNameInputValidation.GetSanitizedFolderCollisionMessages(candidates.Select(g => g.GameName));

    public async Task<BackupGamesOutcome> BackupGamesAsync(IReadOnlyList<GameRowViewModel> selected)
    {
        if (IsBusy || IsScanning)
        {
            return new BackupGamesOutcome("Busy. Wait for current work to finish.", 0, 0, false);
        }

        var candidates = GetBackupCandidates(selected);
        if (candidates.Count == 0)
        {
            return new BackupGamesOutcome(
                selected.Count > 0
                    ? "No selected rows have a valid save folder or registry save to back up."
                    : "No games with valid save folders or registry saves found. Run scan first.",
                0,
                0,
                false);
        }

        var backupRoot = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            return new BackupGamesOutcome("Set a default backup folder in Settings first.", 0, 0, false);
        }

        ScanProgress = 0;
        var retention = Math.Max(1, _settings.Get("backup_retention_count", 3));
        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        var total = candidates.Count;
        var workClock = Stopwatch.StartNew();
        double progressAfterWork = 0;
        var ok = 0;
        var fail = 0;
        var cancelled = false;
        _footerCancelSlot = FooterCancelSlot.Backup;
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        OnPropertyChanged(nameof(CanCancelOperation));
        try
        {
            var token = _operationCts.Token;
            try
            {
                // Copying save trees is synchronous I/O ÔÇö must not run on the UI thread or WinUI freezes.
                await Task.Run(
                    () =>
                    {
                        var step = 0;
                        foreach (var row in candidates)
                        {
                            token.ThrowIfCancellationRequested();
                            step++;
                            var stepCopy = step;
                            var rowCopy = row;
                            EnqueueUi(() =>
                            {
                                StatusText = $"Backing up… ({stepCopy}/{total}) {rowCopy.GameName}";
                                ScanProgress = total == 0 ? 0 : (double)stepCopy / total * 100.0;
                            });

                            if (!_backupCoordinator.TryBegin(rowCopy.GameName))
                            {
                                fail++;
                                _sandboxLog.Log(
                                    "warn",
                                    $"Backup skipped ({rowCopy.GameName}): another backup is already in progress.");
                                progressAfterWork = total == 0 ? 0 : (double)step / total * 100.0;
                                var pSkip = progressAfterWork;
                                EnqueueUi(() => { ScanProgress = pSkip; });
                                continue;
                            }

                            string? err;
                            try
                            {
                                if (rowCopy.SaveInRegistryOnly)
                                {
                                    _registryBackup.BackupToRetentionFile(
                                        rowCopy.GameName,
                                        rowCopy.SaveRegistryHive!,
                                        rowCopy.SaveRegistrySubkey!,
                                        backupRoot,
                                        retention,
                                        subfolder,
                                        token,
                                        out _,
                                        out err);
                                }
                                else
                                {
                                    _folderBackup.BackupToRetentionFolder(
                                        rowCopy.GameName,
                                        rowCopy.SavePathResolved!,
                                        backupRoot,
                                        retention,
                                        subfolder,
                                        token,
                                        out _,
                                        out err);
                                }
                            }
                            finally
                            {
                                _backupCoordinator.End(rowCopy.GameName);
                            }

                            progressAfterWork = total == 0 ? 0 : (double)step / total * 100.0;
                            if (string.IsNullOrWhiteSpace(err))
                            {
                                var nowIso = DateTime.UtcNow.ToString("O");
                                _catalogManager.UpdateLastBackup(rowCopy.GameName, nowIso);
                                ok++;
                                EnqueueUi(() =>
                                {
                                    rowCopy.LastBackupIntegrityWarning = false;
                                    rowCopy.LastBackupCheckpointWarning = false;
                                    rowCopy.LastBackup = FormatLastBackup(nowIso);
                                    RefreshBackupSizeForRow(rowCopy);
                                });
                            }
                            else
                            {
                                fail++;
                                _sandboxLog.Log("warn", $"Backup failed ({rowCopy.GameName}): {err}");
                            }

                            var p = progressAfterWork;
                            EnqueueUi(() => { ScanProgress = p; });
                        }
                    },
                    token).ConfigureAwait(true);

                workClock.Stop();
                await PresentBackupProgressFinishAsync(workClock.ElapsedMilliseconds, progressAfterWork, total).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                workClock.Stop();
                StatusText = "Backup cancelled.";
            }
        }
        finally
        {
            _footerCancelSlot = FooterCancelSlot.None;
            _operationCts.Dispose();
            _operationCts = null;
            OnPropertyChanged(nameof(CanCancelOperation));
            ScanProgress = 0;
            IsBusy = false;
        }

        if (cancelled)
        {
            return new BackupGamesOutcome("Backup cancelled.", ok, fail, true);
        }

        var msg = $"Backup complete: {ok} succeeded";
        if (fail > 0)
        {
            msg += $", {fail} failed";
        }

        msg += ".";
        StatusText = msg;
        _ = RefreshBackupSizeDisplaysAsync();
        return new BackupGamesOutcome(msg, ok, fail, false);
    }

    private void RefreshBackupSizeForRow(GameRowViewModel row)
    {
        var dest = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(dest))
        {
            row.BackupSizeDisplay = GsbtUiText.EmDash;
            return;
        }

        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        try
        {
            var bytes = BackupRetentionVerifier.ComputeTotalRetentionBackupBytes(dest, row.GameName, subfolder);
            row.BackupSizeDisplay = bytes <= 0 ? GsbtUiText.EmDash : BackupFolderSizeEstimator.FormatApproximateSize(bytes);
        }
        catch
        {
            row.BackupSizeDisplay = GsbtUiText.EmDash;
        }
    }

    /// <summary>When disk work finishes in a flash, run a visible sweep so the footer bar reads as ÔÇ£done.ÔÇØ</summary>
    private async Task PresentBackupProgressFinishAsync(long workElapsedMs, double progressAfterWork, int gameCount)
    {
        if (gameCount <= 0)
        {
            return;
        }

        const int fastThresholdMs = 750;
        const int simulatedSweepMs = 640;

        if (workElapsedMs < fastThresholdMs)
        {
            ScanProgress = 0;
            await RampProgressAsync(0, 100, simulatedSweepMs);
            await Task.Delay(90);
            return;
        }

        if (progressAfterWork >= 99.5)
        {
            ScanProgress = 100;
            await Task.Delay(200);
            return;
        }

        await RampProgressAsync(Math.Clamp(progressAfterWork, 0, 100), 100, 280);
        await Task.Delay(90);
    }

    private async Task RampProgressAsync(double from, double to, int totalMs)
    {
        totalMs = Math.Max(80, totalMs);
        var steps = Math.Clamp(totalMs / 22, 10, 40);
        var slice = totalMs / steps;
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var eased = t * t * (3 - 2 * t);
            ScanProgress = from + (to - from) * eased;
            await Task.Delay(slice);
        }

        ScanProgress = to;
    }

    public CompressionOptions GetCompressionOptionsForBackupRun() =>
        CompressionOptionsResolver.FromSettings(
            (k, d) => _settings.Get(k, d),
            (k, d) => _settings.Get(k, d));

    /// <summary>
    /// Settings ÔÇ£is 7-Zip installed?ÔÇØ hint ÔÇö simulation modes override real detection for the Compression UI only.
    /// Actual compress still resolves a real <c>7z.exe</c> path.
    /// </summary>
    public bool HasSevenZipForSettingsHint()
    {
        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulatePresent)
        {
            return true;
        }

        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulateAbsent)
        {
            return false;
        }

        return ResolveSevenZipForOperations() is not null;
    }

    public string? ResolveSevenZipForOperations()
    {
        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulatePresent)
        {
            return SevenZipLocator.FindSevenZipExecutable();
        }

        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulateAbsent)
        {
            return null;
        }

        var custom = (_settings.Get("compression_7z_path", string.Empty) ?? string.Empty).Trim().Trim('"');
        return SevenZipLocator.ResolveSevenZipExe(custom);
    }

    /// <summary>One or two sentences for Settings ÔåÆ Compression (ÔÇ£7-Zip on this PCÔÇØ).</summary>
    public string GetSevenZipInstallStatusUiText()
    {
        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulatePresent)
        {
            return "7-Zip: Installed (simulated for this window).";
        }

        if (_overrides.SevenZipUiOverride == SandboxSevenZipUiMode.SimulateAbsent)
        {
            return "7-Zip: Not installed (simulated for this window).";
        }

        var custom = (_settings.Get("compression_7z_path", string.Empty) ?? string.Empty).Trim().Trim('"');
        var resolved = ResolveSevenZipForOperations();
        if (resolved is not null)
        {
            return $"7-Zip: Installed ({resolved}).";
        }

        if (custom.Length > 0)
        {
            return "7-Zip: Custom path is set but not used (only Program Files\\7-Zip\\7z.exe is accepted). Clear the path or use Get 7-Zip.";
        }

        return "7-Zip: Not installed.";
    }

    /// <summary>
    /// Download pinned 7-Zip + silent install. Logs to <see cref="SandboxLogHub"/> category <c>7zip</c> ÔÇö open Sandbox monitor Live log when testing with <c>-sandbox</c>.
    /// </summary>
    public async Task<string> InstallSevenZipFromOfficialSiteAsync(IProgress<(int percent, string? text)>? ui, CancellationToken cancellationToken)
    {
        void Log(string m) => _sandboxLog.Log("7zip", m);
        if (App.IsSandboxSimulationChild)
        {
            Log("=== Get 7-Zip: skipped in simulation window (no installer download) ===");
            return "7-Zip download and install are disabled in the simulation window. Use the full app to install 7-Zip, or use Sandbox monitor ÔåÆ Simulated states to override ÔÇ£installed / not installedÔÇØ for the Compression UI.";
        }

        Log("=== Get 7-Zip: start (category 7zip; use -sandbox + monitor to watch) ===");
        try
        {
            var (_, fileName) = SevenZipDownloadInstall.PinnedInstallerUrlAndName();
            var tmp = Path.Combine(Path.GetTempPath(), "gsbt_" + fileName);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
            await SevenZipDownloadInstall.DownloadInstallerAsync(
                    http,
                    tmp,
                    new Progress<(long done, long? total)>(v =>
                    {
                        if (v.total is > 0)
                        {
                            var pct = (int)(100 * v.done / v.total.Value);
                            ui?.Report((pct, $"DownloadingÔÇª {pct}%"));
                        }
                    }),
                    cancellationToken,
                    Log)
                .ConfigureAwait(false);

            ui?.Report((95, "Running installer (UAC may appear)ÔÇª"));
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip");
            var (code, mode) = await SevenZipDownloadInstall.InstallSilentAsync(tmp, dir, Log).ConfigureAwait(false);
            if (code != 0)
            {
                return $"7-Zip installer exited with code {code} ({mode}). See Live log category 7zip.";
            }

            var found = await SevenZipDownloadInstall.WaitForSevenZipExeAsync(TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(350), Log).ConfigureAwait(false);
            if (found is null)
            {
                return "Install finished but 7z.exe was not detected in time. Try Compress again or set path to 7z.exe.";
            }

            _settings.Set("compression_7z_path", string.Empty);
            Log($"=== Get 7-Zip: done ÔåÆ {found} ===");
            return $"7-Zip installed: {found}";
        }
        catch (OperationCanceledException)
        {
            return "7-Zip download/install cancelled.";
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            return $"7-Zip install failed: {ex.Message}";
        }
    }

    /// <summary>Effective backup root for compress-on-exit and benchmarks (respects sandbox ÔÇ£no pathÔÇØ).</summary>
    public string? GetEffectiveBackupRootForCompressPrompt()
    {
        if (_overrides.SimulateNoBackupDestination)
        {
            return null;
        }

        return ResolveBackupDestination();
    }

    public async Task<string> CompressBackupFolderAsync(IReadOnlyList<GameRowViewModel>? selected = null)
    {
        var (message, _) = await CompressBackupFolderWithResultAsync(selected).ConfigureAwait(true);
        return message;
    }

    /// <summary>Same as <see cref="CompressBackupFolderAsync"/> but returns structured metrics for sandbox / exit flows.</summary>
    public async Task<(string Message, BackupCompressionResult? Result)> CompressBackupFolderWithResultAsync(
        IReadOnlyList<GameRowViewModel>? selected = null)
    {
        if (IsBusy || IsScanning)
        {
            return ("Busy. Wait for current work to finish.", null);
        }

        var backupRoot = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
        {
            return ("No valid backup folder found. Set default backup path in Settings.", null);
        }

        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        var compressCandidates = GetCompressCandidates(selected);
        if (compressCandidates.Count == 0)
        {
            return selected is { Count: > 0 }
                ? ("No selected games have backup data to compress.", null)
                : ("No backup data found to compress. Run Backup first.", null);
        }

        HashSet<string>? sanitizedFilter = null;
        if (selected is { Count: > 0 })
        {
            sanitizedFilter = compressCandidates
                .Select(g => GameNameInputValidation.SanitizeForWindowsPathSegment(g.GameName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var opts = GetCompressionOptionsForBackupRun();
        if (opts.Engine == "7z" && string.IsNullOrEmpty(opts.SevenZipExe))
        {
            return ("The compression preset uses 7-Zip, but 7z.exe was not found. Install 7-Zip, use Get 7-Zip in Settings ÔåÆ Compression, or set path to 7z.exe.", null);
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var ext = opts.Engine == "7z"
            ? (opts.SevenArchiveFormat is "zip" or "7z" ? opts.SevenArchiveFormat : "7z")
            : "zip";
        var archiveName = $"Backups_{stamp}.{ext}";
        var outPath = Path.Combine(backupRoot, archiveName);

        _footerCancelSlot = FooterCancelSlot.Compress;
        _operationCts = new CancellationTokenSource();
        IsBusy = true;
        OnPropertyChanged(nameof(CanCancelOperation));
        try
        {
            var token = _operationCts.Token;
            try
            {
                var progress = new Progress<int>(pct =>
                    _dispatcher.TryEnqueue(() => ScanProgress = pct));
                _sandboxLog.Log("compress", $"Start {opts.SummaryLabel} ÔåÆ {archiveName}");
                _compressionActivity.Clear();
                var result = await Task.Run(
                        async () =>
                            await _compression.CompressBackupFolderAsync(
                                    backupRoot,
                                    opts,
                                    progress,
                                    msg => _sandboxLog.Log("compress", msg),
                                    folder => _compressionActivity.SetCurrentGameFolder(folder),
                                    token,
                                    subfolder,
                                    sanitizedFilter)
                                .ConfigureAwait(false))
                    .ConfigureAwait(true);
                ScanProgress = 100;
                if (!result.Success)
                {
                    try
                    {
                        if (File.Exists(outPath))
                        {
                            File.Delete(outPath);
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    StatusText = result.Message;
                    return (result.Message, result);
                }

                StatusText = $"Compressed: {result.ArchivePath}";
                return (StatusText, result);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Compress cancelled.";
                try
                {
                    if (File.Exists(outPath))
                    {
                        File.Delete(outPath);
                    }
                }
                catch
                {
                    // ignore partial cleanup
                }

                return (StatusText, null);
            }
            catch (Exception ex)
            {
                StatusText = $"Compress failed: {ex.Message}";
                return (StatusText, null);
            }
        }
        finally
        {
            _compressionActivity.Clear();
            _footerCancelSlot = FooterCancelSlot.None;
            _operationCts.Dispose();
            _operationCts = null;
            OnPropertyChanged(nameof(CanCancelOperation));
            IsBusy = false;
            _dispatcher.TryEnqueue(() => ScanProgress = 0);
        }
    }
    public async Task RefreshBackupSizeDisplaysAsync(CancellationToken cancellationToken = default)
    {
        var rows = Games.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var dest = ResolveBackupDestination();
        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        var updates = await Task.Run(() =>
        {
            var list = new List<(GameRowViewModel Row, string Display)>(rows.Count);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(dest))
                {
                    list.Add((row, GsbtUiText.EmDash));
                    continue;
                }

                var bytes = BackupRetentionVerifier.ComputeTotalRetentionBackupBytes(dest, row.GameName, subfolder);
                list.Add((row, bytes <= 0 ? GsbtUiText.EmDash : BackupFolderSizeEstimator.FormatApproximateSize(bytes)));
            }

            return list;
        }, cancellationToken).ConfigureAwait(true);

        EnqueueUi(() =>
        {
            foreach (var (row, display) in updates)
            {
                row.BackupSizeDisplay = display;
            }
        });
    }

    /// <summary>Resolved backup root for UI (forward slashes).</summary>
    public string? BackupDestinationDisplayPath =>
        ResolveBackupDestination()?.Replace('\\', '/');

    /// <summary>User dismissed large-folder warnings for this title (persisted).</summary>
    public bool IsLargeSavePathTrusted(string gameName) =>
        GetTrustedLargeSavePaths().Contains(gameName);

    /// <summary>Marks a save path as trusted so future estimates/backups donÔÇÖt warn for size on this title.</summary>
    public void TrustLargeSavePath(string gameName)
    {
        var name = (gameName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var list = _settings.Get("trusted_large_save_paths", new List<string>());
        var set = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        if (!set.Add(name))
        {
            return;
        }

        _settings.Set("trusted_large_save_paths", set.ToList());
    }

    /// <summary>Clears persisted large-save trust for a title (e.g. after removing it from the catalog so a rescan prompts again).</summary>
    public void UntrustLargeSavePath(string gameName)
    {
        var name = (gameName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var list = _settings.Get("trusted_large_save_paths", new List<string>());
        var set = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        if (!set.Remove(name))
        {
            return;
        }

        _settings.Set("trusted_large_save_paths", set.ToList());
    }

    private HashSet<string> GetTrustedLargeSavePaths() =>
        new(_settings.Get("trusted_large_save_paths", new List<string>()), StringComparer.OrdinalIgnoreCase);

    private static (long Bytes, int Files) GetSimulatedDirectoryMetricsForSandboxChild(
        string gameName,
        string saveRoot,
        CancellationToken cancellationToken)
    {
        if (string.Equals(gameName, "Game B", StringComparison.OrdinalIgnoreCase))
        {
            return (BackupFolderSizeEstimator.LargeSaveThresholdBytes, 64);
        }

        if (string.Equals(gameName, "Game C", StringComparison.OrdinalIgnoreCase))
        {
            return (BackupFolderSizeEstimator.SuspiciousSaveThresholdBytes + 1024L * 1024, 100);
        }

        return BackupFolderSizeEstimator.ComputeDirectoryMetrics(saveRoot, cancellationToken);
    }

    /// <summary>
    /// Walks save folders for the same scope as <see cref="BackupGamesAsync"/> (counts, sizes, registry-only rows). Runs off the UI thread.
    /// </summary>
    public async Task<BackupSizeEstimateSummary?> ComputeBackupEstimateAsync(
        IReadOnlyList<GameRowViewModel> selected,
        CancellationToken cancellationToken = default)
    {
        var scope = selected.Count > 0 ? selected.ToList() : Games.ToList();
        var diskCandidates = scope.Where(CanBackupDiskSave).ToList();
        var registryOnly = scope.Where(CanBackupRegistrySave).ToList();

        if (diskCandidates.Count == 0 && registryOnly.Count == 0)
        {
            return null;
        }

        var trusted = GetTrustedLargeSavePaths();
        var destDisplay = BackupDestinationDisplayPath ?? string.Empty;

        return await Task.Run(
            () =>
            {
                var entries = new List<BackupSizeEstimateEntry>();
                long totalBytes = 0;
                var totalFiles = 0;

                foreach (var row in diskCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long bytes;
                    int files;
                    if (App.IsSandboxSimulationChild)
                    {
                        (bytes, files) = GetSimulatedDirectoryMetricsForSandboxChild(row.GameName, row.SavePathResolved!, cancellationToken);
                    }
                    else
                    {
                        (bytes, files) = BackupFolderSizeEstimator.ComputeDirectoryMetrics(row.SavePathResolved!, cancellationToken);
                    }

                    totalBytes += bytes;
                    totalFiles += files;
                    var raw = BackupFolderSizeEstimator.Classify(bytes);
                    var severity = trusted.Contains(row.GameName) ? BackupSizeSeverity.Normal : raw;
                    entries.Add(new BackupSizeEstimateEntry(row.GameName, bytes, files, false, severity, row.SavePathResolved));
                }

                foreach (var row in registryOnly)
                {
                    entries.Add(new BackupSizeEstimateEntry(row.GameName, 0, 0, true, BackupSizeSeverity.Normal));
                }

                entries.Sort((a, b) =>
                {
                    if (a.IsRegistryOnly != b.IsRegistryOnly)
                    {
                        return a.IsRegistryOnly ? 1 : -1;
                    }

                    return b.Bytes.CompareTo(a.Bytes);
                });

                return new BackupSizeEstimateSummary(
                    totalBytes,
                    totalFiles,
                    scope.Count,
                    diskCandidates.Count,
                    registryOnly.Count,
                    destDisplay,
                    entries);
            },
            cancellationToken).ConfigureAwait(false);
    }
    private static bool CanBackupDiskSave(GameRowViewModel row) =>
        !row.SaveInRegistryOnly
        && !string.IsNullOrWhiteSpace(row.SavePathResolved)
        && Directory.Exists(row.SavePathResolved);

    private static bool CanBackupRegistrySave(GameRowViewModel row) =>
        row.SaveInRegistryOnly
        && !string.IsNullOrWhiteSpace(row.SaveRegistryHive)
        && !string.IsNullOrWhiteSpace(row.SaveRegistrySubkey)
        && RegistrySaveBackupService.TryComputeSnapshotFingerprint(
            row.SaveRegistryHive,
            row.SaveRegistrySubkey,
            out _);

    private static bool CanBackupRow(GameRowViewModel row) =>
        CanBackupDiskSave(row) || CanBackupRegistrySave(row);

    /// <summary>Toolbar / context-menu guard for starting a backup on one row.</summary>
    public bool CanBackupRowForUi(GameRowViewModel row) => CanBackupRow(row);

    public IReadOnlyList<GameRowViewModel> GetCompressCandidates(IReadOnlyList<GameRowViewModel>? selected)
    {
        var backupRoot = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            return [];
        }

        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        var pool = selected is { Count: > 0 }
            ? selected
            : Games;
        return pool.Where(g => CanCompressRow(g, backupRoot, subfolder)).ToList();
    }

    public bool CanCompressRowForUi(GameRowViewModel row)
    {
        var backupRoot = ResolveBackupDestination();
        return !string.IsNullOrWhiteSpace(backupRoot) && CanCompressRow(row, backupRoot, _settings.Get("backup_subfolder_per_game", true));
    }

    private static bool CanCompressRow(GameRowViewModel row, string backupRoot, bool subfolderPerGame) =>
        BackupRetentionVerifier.HasRetentionArtifact(backupRoot, row.GameName, subfolderPerGame);

    private string? ResolveBackupDestination()
    {
        var d = _settings.Get("default_backup_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(d))
        {
            try
            {
                Directory.CreateDirectory(d);
                return d;
            }
            catch
            {
                // ignore invalid configured path and fall through
            }
        }

        var last = _settings.Get("last_backup_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(last))
        {
            try
            {
                Directory.CreateDirectory(last);
                return last;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
