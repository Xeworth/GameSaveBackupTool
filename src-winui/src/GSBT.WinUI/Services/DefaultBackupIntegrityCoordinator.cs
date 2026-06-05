using Microsoft.UI.Dispatching;

namespace GSBT.WinUI.Services;

/// <summary>
/// Watches only <see cref="SettingsStore"/> default backup path (debounced file-system notifications).
/// Uses a slow poll fallback when the folder is missing or a watcher cannot be created (cheap Directory.Exists).
/// </summary>
public sealed class DefaultBackupIntegrityCoordinator : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _reconcile;

    private FileSystemWatcher? _watcher;
    private DispatcherQueueTimer? _debounceTimer;
    private DispatcherQueueTimer? _slowPollTimer;

    private const int DebounceMs = 280;
    private const int SlowPollSeconds = 20;

    public DefaultBackupIntegrityCoordinator(SettingsStore settings, DispatcherQueue dispatcher, Action reconcile)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _reconcile = reconcile;
    }

    /// <summary>Recreate the watcher when settings change or after reconciliation.</summary>
    public void RefreshWatcherPath()
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(RefreshWatcherPath);
            return;
        }

        TearDownWatcher();

        var path = (_settings.Get("default_backup_path", string.Empty) ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            SyncSlowPoll(false);
            return;
        }

        if (!Directory.Exists(path))
        {
            SyncSlowPoll(true);
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 262_144,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Created += OnFsChanged;
            _watcher.Deleted += OnFsChanged;
            _watcher.Changed += OnFsChanged;
            _watcher.Renamed += OnFsRenamed;
            _watcher.Error += (_, _) => DebouncedReconcile();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            TearDownWatcher();
        }

        var needFallbackPoll = !Directory.Exists(path) || _watcher is null;
        SyncSlowPoll(needFallbackPoll);
    }

    private void TearDownWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch
        {
            // ignore
        }

        _watcher = null;
    }

    private void OnFsChanged(object sender, FileSystemEventArgs e) => DebouncedReconcile();

    private void OnFsRenamed(object sender, RenamedEventArgs e) => DebouncedReconcile();

    private void DebouncedReconcile()
    {
        _dispatcher.TryEnqueue(() =>
        {
            _debounceTimer ??= _dispatcher.CreateTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick -= DebounceFire;
            _debounceTimer.Tick += DebounceFire;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void DebounceFire(DispatcherQueueTimer sender, object args)
    {
        sender.Tick -= DebounceFire;
        _reconcile();
    }

    private void SyncSlowPoll(bool enable)
    {
        if (enable)
        {
            if (_slowPollTimer != null)
            {
                return;
            }

            _slowPollTimer = _dispatcher.CreateTimer();
            _slowPollTimer.Interval = TimeSpan.FromSeconds(SlowPollSeconds);
            _slowPollTimer.IsRepeating = true;
            _slowPollTimer.Tick += SlowPollTick;
            _slowPollTimer.Start();
            // Root missing or watcher failed: still run once now so integrity/toasts are not delayed until first poll tick.
            _reconcile();
        }
        else if (_slowPollTimer != null)
        {
            _slowPollTimer.Stop();
            _slowPollTimer.Tick -= SlowPollTick;
            _slowPollTimer = null;
        }
    }

    private void SlowPollTick(DispatcherQueueTimer sender, object args) => _reconcile();

    public void Dispose()
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(DisposeInner);
            return;
        }

        DisposeInner();
    }

    private void DisposeInner()
    {
        TearDownWatcher();
        SyncSlowPoll(false);
        if (_debounceTimer != null)
        {
            _debounceTimer.Stop();
            _debounceTimer.Tick -= DebounceFire;
            _debounceTimer = null;
        }
    }
}
