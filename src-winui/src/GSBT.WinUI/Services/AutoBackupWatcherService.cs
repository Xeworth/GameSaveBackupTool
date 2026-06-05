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

    private readonly object _gate = new();

    private DispatcherQueueTimer? _pollTimer;

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
                    () =>
                    {
                        _folderBackup.BackupToRetentionFolder(
                            gameName,
                            resolved,
                            dest,
                            retention,
                            subfolder,
                            CancellationToken.None,
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

    private void OnSaveActivity(string gameName, Func<string?> runBackup)
    {
        var frequencyMinutes = Math.Max(1, _settings.Get("backup_frequency_minutes", 5));
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_lastBackupUtc.TryGetValue(gameName, out var last))
            {
                if (now - last < TimeSpan.FromMinutes(frequencyMinutes))
                {
                    return;
                }
            }

        }

        if (!_backupCoordinator.TryBegin(gameName))
        {
            return;
        }

        _notifyUser?.Invoke($"Backing up {gameName}…");

        _ = Task.Run(() =>
        {
            try
            {
                var err = runBackup();
                _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(err))
                        {
                            _sandboxLog?.Log("warn", $"Auto-backup failed ({gameName}): {err}");
                            return;
                        }

                        var iso = DateTime.UtcNow.ToString("O");
                        _catalog.UpdateLastBackup(gameName, iso);
                        _catalog.Flush();
                        MarkBackupCooldown(gameName);
                        _onBackupSucceeded?.Invoke(gameName);
                        _sandboxLog?.Log("info", $"Auto-backup finished: {gameName}");
                        _notifyUser?.Invoke($"Backed up {gameName}");
                    }
                    finally
                    {
                        _backupCoordinator.End(gameName);
                    }
                });
            }
            catch (Exception ex)
            {
                _dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        _sandboxLog?.Log("warn", $"Auto-backup failed ({gameName}): {ex.Message}");
                    }
                    finally
                    {
                        _backupCoordinator.End(gameName);
                    }
                });
            }
        });
    }

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
                    out var fingerprint))
            {
                _registryFingerprints.Remove(gameName);
                _sandboxLog?.Log("warn", $"Registry key missing for \"{gameName}\" — will retry on next poll.");
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

            _ = Task.Run(() =>
            {
                try
                {
                    _registryBackup.BackupToRetentionFile(
                        gameName,
                        hive,
                        subkey,
                        dest,
                        retention,
                        subfolder,
                        CancellationToken.None,
                        out _,
                        out var err);

                    _dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(err))
                            {
                                _sandboxLog?.Log("warn", $"Registry auto-backup failed ({gameName}): {err}");
                                return;
                            }

                            _registryFingerprints[gameName] = capturedFingerprint;
                            var iso = DateTime.UtcNow.ToString("O");
                            _catalog.UpdateLastBackup(gameName, iso);
                            _catalog.Flush();
                            MarkBackupCooldown(gameName);
                            _onBackupSucceeded?.Invoke(gameName);
                            _sandboxLog?.Log("info", $"Registry auto-backup finished: {gameName}");
                            _notifyUser?.Invoke($"Backed up {gameName}");
                        }
                        finally
                        {
                            _backupCoordinator.End(gameName);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            _sandboxLog?.Log("warn", $"Registry auto-backup failed ({gameName}): {ex.Message}");
                        }
                        finally
                        {
                            _backupCoordinator.End(gameName);
                        }
                    });
                }
            });
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
