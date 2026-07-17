using GSBT.Core.Services;
using Microsoft.UI.Dispatching;

namespace GSBT.WinUI.Services;

/// <summary>
/// Watchdog parity with Python: folder <see cref="FileSystemWatcher"/>, registry poll snapshots,
/// cooldown from Settings, retention pruning, 30s alignment poll, and recovery from watcher errors.
/// </summary>
public sealed class AutoBackupWatcherService : IDisposable
{
    private const int PollIntervalSeconds = 30;

    /// <summary>Cap live folder watchers; additional saves rely on the poll timer only.</summary>
    private const int MaxFolderWatchers = 64;

    private readonly SettingsStore _settings;
    private readonly SaveCatalogManager _catalog;
    private readonly SaveFolderBackupService _folderBackup = new();
    private readonly RegistrySaveBackupService _registryBackup = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly SandboxLogHub? _sandboxLog;
    private readonly Action<string>? _onBackupSucceeded;
    private readonly Action<string>? _notifyUser;
    private readonly GameBackupCoordinator _backupCoordinator;

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RegistrySaveBackupService.RegistrySaveTarget> _registryTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _registryFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastBackupUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _debounceTokens = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    private DispatcherQueueTimer? _pollTimer;
    private bool _disposed;

    public AutoBackupWatcherService(
        SettingsStore settings,
        SaveCatalogManager catalog,
        DispatcherQueue dispatcher,
        GameBackupCoordinator backupCoordinator,
        SandboxLogHub? sandboxLog = null,
        Action<string>? onBackupSucceeded = null,
        Action<string>? notifyUser = null)
    {
        _settings = settings;
        _catalog = catalog;
        _dispatcher = dispatcher;
        _backupCoordinator = backupCoordinator;
        _sandboxLog = sandboxLog;
        _onBackupSucceeded = onBackupSucceeded;
        _notifyUser = notifyUser;
    }

    /// <summary>Rebuild watchers from the live catalog and current Settings (frequency, retention, paths).</summary>
    public void RestartMonitoringIfNeeded()
    {
        var folderCount = 0;
        var registryCount = 0;
        lock (_gate)
        {
            StopMonitoringUnsafe();

            if (!_settings.Get("auto_backup_enabled", false))
            {
                _sandboxLog?.Log("info", "Auto-backup is off (Settings).");
                SyncPollTimerUnsafe();
                return;
            }

            var dest = ResolveBackupDestination();
            if (string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest))
            {
                _sandboxLog?.Log("warn", "Auto-backup: no valid backup folder (set default or last backup path in Settings).");
                SyncPollTimerUnsafe();
                return;
            }

            var frequency = Math.Max(1, _settings.Get("backup_frequency_minutes", 5));
            var retention = Math.Max(1, _settings.Get("backup_retention_count", 3));
            var subfolder = _settings.Get("backup_subfolder_per_game", true);

            var folderWatcherBudget = MaxFolderWatchers;
            foreach (var (gameName, row) in _catalog.Catalog)
            {
                if (RegistrySaveBackupService.TryGetTargetFromCatalogRow(row, out var regTarget))
                {
                    if (!RegistrySaveBackupService.IsRegistryTargetSafe(regTarget.Hive, regTarget.Subkey))
                    {
                        _sandboxLog?.Log("warn", $"Registry save skipped for \"{gameName}\" (invalid or inaccessible key).");
                        continue;
                    }

                    if (RegistrySaveBackupService.TryComputeSnapshotFingerprint(
                            regTarget.Hive,
                            regTarget.Subkey,
                            out _))
                    {
                        _registryTargets[gameName] = regTarget;
                        _registryFingerprints.Remove(gameName);
                        registryCount++;
                        _sandboxLog?.Log("scan", $"Registry poll for \"{gameName}\" ({regTarget.Hive}\\{regTarget.Subkey}).");
                    }
                    else
                    {
                        _sandboxLog?.Log("warn", $"Registry save key unavailable for \"{gameName}\".");
                    }

                    continue;
                }

                var raw = row.TryGetValue("save_path", out var sp) ? sp?.ToString() : null;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var resolved = _catalog.ResolvePath(raw, null);
                if (string.IsNullOrWhiteSpace(resolved) || !Directory.Exists(resolved))
                {
                    continue;
                }

                if (folderWatcherBudget > 0)
                {
                    TryAddFolderWatcher(gameName, resolved, dest, retention, subfolder, frequency);
                    if (_watchers.ContainsKey(gameName))
                    {
                        folderWatcherBudget--;
                    }
                }
            }

            folderCount = _watchers.Count;
            var eligibleFolders = CountEligibleFolderSaves();
            if (eligibleFolders > folderCount && folderCount >= MaxFolderWatchers)
            {
                _sandboxLog?.Log(
                    "warn",
                    $"Auto-backup: folder watcher limit ({MaxFolderWatchers}) reached — {eligibleFolders - folderCount} save(s) rely on poll-only detection ({PollIntervalSeconds}s).");
            }
            registryCount = _registryTargets.Count;
            if (folderCount > 0 || registryCount > 0)
            {
                _sandboxLog?.Log(
                    "info",
                    $"Auto-backup monitoring {folderCount} folder(s) and {registryCount} registry save(s).");
            }
            else
            {
                _sandboxLog?.Log("scan", "Auto-backup enabled, but no save folders or registry saves in catalog to watch yet.");
            }

            SyncPollTimerUnsafe();
        }

        if (folderCount > 0 || registryCount > 0)
        {
            TryNotifyMonitoringStarted(folderCount, registryCount);
        }

        if (registryCount > 0)
        {
            PollRegistrySavesUnsafe();
        }
    }

    private void TryNotifyMonitoringStarted(int folderCount, int registryCount)
    {
        var parts = new List<string>();
        if (folderCount > 0)
        {
            parts.Add($"{folderCount} save folder(s)");
        }

        if (registryCount > 0)
        {
            parts.Add($"{registryCount} registry save(s)");
        }

        _notifyUser?.Invoke($"Auto-backup active — watching {string.Join(" and ", parts)}.");
    }

    private void TryAddFolderWatcher(string gameName, string resolved, string dest, int retention, bool subfolder, int frequencyMinutes)
    {
        try
        {
            var w = new FileSystemWatcher(resolved)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = false,
            };

            void Handler(object _, FileSystemEventArgs e)
            {
                if (!IsNonDirectoryFileEvent(e.FullPath))
                {
                    return;
                }

                OnSaveActivity(
                    gameName,
                    token =>
                    {
                        _folderBackup.BackupToRetentionFolder(
                            gameName,
                            resolved,
                            dest,
                            retention,
                            subfolder,
                            token,
                            out string _,
                            out var err);
                        return err;
                    });
            }

            void HandlerRenamed(object _, RenamedEventArgs e)
            {
                if (!IsNonDirectoryFileEvent(e.FullPath))
                {
                    return;
                }

                Handler(_, e);
            }

            void OnErr(object _, ErrorEventArgs e)
            {
                _sandboxLog?.Log("warn", $"Save watcher buffer/error for \"{gameName}\": {e.GetException()?.Message ?? "error"}");
                _dispatcher.TryEnqueue(RestartMonitoringIfNeeded);
            }

            w.Changed += Handler;
            w.Created += Handler;
            w.Renamed += HandlerRenamed;
            w.Error += OnErr;
            w.EnableRaisingEvents = true;
            _watchers[gameName] = w;
            _sandboxLog?.Log("scan", $"Watching saves for \"{gameName}\".");
        }
        catch (Exception ex)
        {
            _sandboxLog?.Log("warn", $"Could not watch \"{gameName}\": {ex.Message}");
        }
    }

    private static bool IsNonDirectoryFileEvent(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                return false;
            }

            return (File.GetAttributes(fullPath) & FileAttributes.Directory) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnSaveActivity(string gameName, Func<CancellationToken, string?> runBackup)
    {
        CancellationTokenSource debounce;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_debounceTokens.Remove(gameName, out var previous))
            {
                previous.Cancel();
            }

            debounce = new CancellationTokenSource();
            _debounceTokens[gameName] = debounce;
        }

        _ = RunDebouncedFolderBackupAsync(gameName, runBackup, debounce);
    }

    private async Task RunDebouncedFolderBackupAsync(
        string gameName,
        Func<CancellationToken, string?> runBackup,
        CancellationTokenSource debounce)
    {
        var beganBackup = false;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), debounce.Token).ConfigureAwait(false);
            var frequencyMinutes = Math.Max(1, _settings.Get("backup_frequency_minutes", 5));
            lock (_gate)
            {
                if (_disposed
                    || _lastBackupUtc.TryGetValue(gameName, out var last)
                    && DateTime.UtcNow - last < TimeSpan.FromMinutes(frequencyMinutes))
                {
                    return;
                }
            }

            beganBackup = _backupCoordinator.TryBegin(gameName);
            if (!beganBackup)
            {
                return;
            }

            _notifyUser?.Invoke($"Backing up {gameName}...");
            string? error = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                debounce.Token.ThrowIfCancellationRequested();
                error = runBackup(debounce.Token);
                if (string.IsNullOrWhiteSpace(error) || !IsTransientBackupError(error) || attempt == 3)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), debounce.Token).ConfigureAwait(false);
            }

            _dispatcher.TryEnqueue(() =>
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    OperationHistoryStore.Record("auto-backup", "failed", error, gameName);
                    _sandboxLog?.Log("warn", $"Auto-backup failed ({gameName}): {error}");
                    return;
                }

                var iso = DateTime.UtcNow.ToString("O");
                _catalog.UpdateLastBackup(gameName, iso);
                OperationHistoryStore.Record("auto-backup", "succeeded", "Auto-backup completed.", gameName);
                _catalog.Flush();
                MarkBackupCooldown(gameName);
                _onBackupSucceeded?.Invoke(gameName);
                _sandboxLog?.Log("info", $"Auto-backup finished: {gameName}");
                _notifyUser?.Invoke($"Backed up {gameName}");
            });
        }
        catch (OperationCanceledException)
        {
            // A newer event replaced this debounce, or monitoring stopped.
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
                _sandboxLog?.Log("warn", $"Auto-backup failed ({gameName}): {ex.Message}"));
        }
        finally
        {
            if (beganBackup)
            {
                _backupCoordinator.End(gameName);
            }

            lock (_gate)
            {
                if (_debounceTokens.TryGetValue(gameName, out var current)
                    && ReferenceEquals(current, debounce))
                {
                    _debounceTokens.Remove(gameName);
                }
            }

            debounce.Dispose();
        }
    }

    private static bool IsTransientBackupError(string error) =>
        error.Contains("used by another process", StringComparison.OrdinalIgnoreCase)
        || error.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
        || error.Contains("being used", StringComparison.OrdinalIgnoreCase)
        || error.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);

    private void MarkBackupCooldown(string gameName)
    {
        lock (_gate)
        {
            _lastBackupUtc[gameName] = DateTime.UtcNow;
        }
    }

    private void PollRegistrySavesUnsafe()
    {
        if (!_settings.Get("auto_backup_enabled", false))
        {
            return;
        }

        var dest = ResolveBackupDestination();
        if (string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest))
        {
            return;
        }

        if (_registryTargets.Count == 0)
        {
            return;
        }

        var retention = Math.Max(1, _settings.Get("backup_retention_count", 3));
        var subfolder = _settings.Get("backup_subfolder_per_game", true);
        var frequency = Math.Max(1, _settings.Get("backup_frequency_minutes", 5));

        foreach (var (gameName, target) in _registryTargets.ToList())
        {
            if (!RegistrySaveBackupService.TryComputeSnapshotFingerprint(
                    target.Hive,
                    target.Subkey,
                    out var fingerprint,
                    out var fingerprintError))
            {
                _registryFingerprints.Remove(gameName);
                _sandboxLog?.Log(
                    "warn",
                    $"Registry save unavailable for \"{gameName}\": {fingerprintError ?? "unknown read error"} Will retry on next poll.");
                continue;
            }

            if (!_registryFingerprints.TryGetValue(gameName, out var previous))
            {
                _registryFingerprints[gameName] = fingerprint;
                continue;
            }

            if (string.Equals(previous, fingerprint, StringComparison.Ordinal))
            {
                continue;
            }

            var now = DateTime.UtcNow;
            if (_lastBackupUtc.TryGetValue(gameName, out var last)
                && now - last < TimeSpan.FromMinutes(frequency))
            {
                continue;
            }

            if (!_backupCoordinator.TryBegin(gameName))
            {
                continue;
            }

            _notifyUser?.Invoke($"Backing up {gameName} (registry)…");
            var hive = target.Hive;
            var subkey = target.Subkey;
            var capturedFingerprint = fingerprint;

            _ = RunRegistryBackupAsync(
                gameName,
                hive,
                subkey,
                dest,
                retention,
                subfolder,
                capturedFingerprint);
        }
    }

    private async Task RunRegistryBackupAsync(
        string gameName,
        string hive,
        string subkey,
        string destination,
        int retention,
        bool subfolder,
        string capturedFingerprint)
    {
        try
        {
            var result = await Task.Run(() => _registryBackup.BackupToRetentionFileWithResult(
                gameName,
                hive,
                subkey,
                destination,
                retention,
                subfolder,
                CancellationToken.None)).ConfigureAwait(false);

            _dispatcher.TryEnqueue(() =>
            {
                if (!result.Success)
                {
                    OperationHistoryStore.Record("auto-backup", "failed", result.Error ?? "Registry backup failed.", gameName);
                    _sandboxLog?.Log("warn", $"Registry auto-backup failed ({gameName}): {result.Error}");
                    return;
                }

                _registryFingerprints[gameName] = capturedFingerprint;
                var iso = DateTime.UtcNow.ToString("O");
                _catalog.UpdateLastBackup(gameName, iso);
                _catalog.Flush();
                MarkBackupCooldown(gameName);
                OperationHistoryStore.Record("auto-backup", "succeeded", "Registry auto-backup completed.", gameName);
                _onBackupSucceeded?.Invoke(gameName);
                _sandboxLog?.Log("info", $"Registry auto-backup finished: {gameName}");
                _notifyUser?.Invoke($"Backed up {gameName}");
            });
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
                _sandboxLog?.Log("warn", $"Registry auto-backup failed ({gameName}): {ex.Message}"));
        }
        finally
        {
            _backupCoordinator.End(gameName);
        }
    }

    private void CheckWatcherStatus()
    {
        var dest = ResolveBackupDestination();
        var auto = _settings.Get("auto_backup_enabled", false);
        var backupReady = !string.IsNullOrWhiteSpace(dest) && Directory.Exists(dest!);

        var catalogHasSave =
            _catalog.Catalog.Values.Any(row =>
            {
                if (row.TryGetValue("save_path", out var sp) && sp is string s && !string.IsNullOrWhiteSpace(s))
                {
                    return true;
                }

                return RegistrySaveBackupService.TryGetTargetFromCatalogRow(row, out _);
            });

        var shouldMonitor = auto && backupReady && catalogHasSave;
        bool isMonitoring;
        lock (_gate)
        {
            isMonitoring = _watchers.Count > 0 || _registryTargets.Count > 0;
        }

        if (shouldMonitor && !isMonitoring)
        {
            RestartMonitoringIfNeeded();
            return;
        }

        if (!shouldMonitor && isMonitoring)
        {
            RestartMonitoringIfNeeded();
            return;
        }

        lock (_gate)
        {
            PollRegistrySavesUnsafe();
        }
    }

    private void PollTimer_Tick(DispatcherQueueTimer sender, object args) => CheckWatcherStatus();

    private void SyncPollTimerUnsafe()
    {
        var want = WatcherPollTimerWantedUnsafe();

        if (want && _pollTimer is null)
        {
            var t = _dispatcher.CreateTimer();
            t.Interval = TimeSpan.FromSeconds(PollIntervalSeconds);
            t.IsRepeating = true;
            t.Tick += PollTimer_Tick;
            t.Start();
            _pollTimer = t;
            _sandboxLog?.Log("info", $"Auto-backup poll timer on ({PollIntervalSeconds}s).");
        }
        else if (!want && _pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= PollTimer_Tick;
            _pollTimer = null;
            _sandboxLog?.Log("info", "Auto-backup poll timer off.");
        }
    }

    private bool WatcherPollTimerWantedUnsafe()
    {
        if (_watchers.Count > 0 || _registryTargets.Count > 0)
        {
            return true;
        }

        if (!_settings.Get("auto_backup_enabled", false))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ResolveBackupDestination());
    }

    private string? ResolveBackupDestination()
    {
        var d = _settings.Get("default_backup_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
        {
            return d;
        }

        var last = _settings.Get("last_backup_path", string.Empty);
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
        {
            return last;
        }

        return null;
    }

    private void StopMonitoringUnsafe()
    {
        foreach (var debounce in _debounceTokens.Values)
        {
            debounce.Cancel();
            debounce.Dispose();
        }

        _debounceTokens.Clear();
        foreach (var w in _watchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }

        _watchers.Clear();
        _registryTargets.Clear();
        _registryFingerprints.Clear();
    }

    private int CountEligibleFolderSaves()
    {
        var n = 0;
        foreach (var row in _catalog.Catalog.Values)
        {
            if (RegistrySaveBackupService.TryGetTargetFromCatalogRow(row, out _))
            {
                continue;
            }

            var raw = row.TryGetValue("save_path", out var sp) ? sp?.ToString() : null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var resolved = _catalog.ResolvePath(raw, null);
            if (!string.IsNullOrWhiteSpace(resolved) && Directory.Exists(resolved))
            {
                n++;
            }
        }

        return n;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_pollTimer is not null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= PollTimer_Tick;
                _pollTimer = null;
            }

            StopMonitoringUnsafe();
        }
    }
}
