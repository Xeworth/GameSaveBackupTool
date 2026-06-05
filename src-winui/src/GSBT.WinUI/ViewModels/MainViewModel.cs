using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using GSBT.Core.Catalog;
using GSBT.Core.Common;
using GSBT.Core.Models;
using GSBT.Core.Services;
using GSBT.WinUI;
using GSBT.WinUI.Common;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private enum FooterCancelSlot
    {
        None,
        Backup,
        Compress
    }

    /// <summary>
    /// After the user runs a scan (or adds a custom game), we persist the grid on disk and restore it on next launch.
    /// Fresh installs keep an empty list until then (no legacy repo catalog import).
    /// </summary>
    private const string SavedGameListEstablishedKey = "saved_game_list_established";

    private readonly SettingsStore _settings;
    private readonly SaveCatalogManager _catalogManager;
    private readonly RegistrySaveResolver _registryResolver;
    private readonly ScanService _scanService;
    private readonly SandboxLogHub _sandboxLog;
    private readonly ISandboxRuntimeOverrides _overrides;
    private readonly GameBackupCoordinator _backupCoordinator;
    private readonly AutoBackupWatcherService _autoBackup;
    private readonly SaveFolderBackupService _folderBackup = new();
    private readonly RegistrySaveBackupService _registryBackup = new();
    private readonly BackupCompressionService _compression = new();
    private readonly CompressionActivityTracker _compressionActivity;
    private readonly DispatcherQueue _dispatcher;
    private readonly DefaultBackupIntegrityCoordinator _backupIntegrityCoordinator;
    private DispatcherQueueTimer? _simulationBackupIntegrityPollTimer;
    private readonly Stack<List<GameRowViewModel>> _undoDelete = new();

    /// <summary>After ÔÇ£replay teaching tips on next launchÔÇØ, allow one bulk-backup tip when the grid already has rows (normal flow is empty ÔåÆ scan tip first).</summary>
    private bool _pendingReplayCheckpointBulkBackup;


    /// <summary>Rows marked selected independent of the filter; reconciled onto <see cref="DisplayedGames"/> after each rebuild.</summary>
    private readonly HashSet<GameRowViewModel> _logicalSelection = [];

    /// <summary>Fired immediately before <see cref="DisplayedGames"/> is cleared during a full rebuild (filter, catalog load, scan).</summary>
    public event EventHandler? DisplayedGamesRebuildStarting;

    /// <summary>Fired after <see cref="DisplayedGames"/> is fully rebuilt (filter change, scan, catalog load).</summary>
    public event EventHandler? DisplayedListRebuilt;

    /// <summary>Raised once after a scan that leaves at least one row, until the bulk-backup teaching tip is dismissed.</summary>
    public event EventHandler? TeachingTipBackupBulkRequested;

    /// <summary>Raised on first launch when the game list is still empty (scan + add-custom onboarding).</summary>
    public event EventHandler? TeachingTipOnboardingScanAddRequested;

    /// <summary>Full scan/catalog rows (master list).</summary>
    public ObservableCollection<GameRowViewModel> Games { get; } = [];

    /// <summary>Rows visible with current filter (bind the main game list here).</summary>
    public ObservableCollection<GameRowViewModel> DisplayedGames { get; } = [];

    [ObservableProperty]
    private string _statusText = "Ready. Click 'Scan for games'.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressStripVisible))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressStripVisible))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressStripVisible))]
    private double _scanProgress;

    [ObservableProperty]
    private GameCatalogFilterMode _filterMode = GameCatalogFilterMode.FoundOnly;

    [ObservableProperty]
    private string _filterButtonText = "Filter: Found";

    /// <summary>Thin progress track under the grid (hidden when idle).</summary>
    public bool IsProgressStripVisible => IsScanning || IsBusy || ScanProgress > 0.5;

    /// <summary>Footer Backup / Compress enabled only when the game list has rows.</summary>
    public bool CanUseBackupAndCompress => Games.Count > 0;

    private CancellationTokenSource? _operationCts;
    private FooterCancelSlot _footerCancelSlot;

    /// <summary>Footer Backup / Compress ÔÇö disabled while backup/compress holds <see cref="IsBusy"/> (except Cancel morph).</summary>
    public bool CanBackupOrCompressFooter => CanUseBackupAndCompress && !IsBusy;

    /// <summary>True while a manual backup is running ÔÇö Backup footer shows Cancel.</summary>
    public bool FooterBackupShowsCancel => IsBusy && _footerCancelSlot == FooterCancelSlot.Backup;

    /// <summary>True while compress is running ÔÇö Compress footer shows Cancel.</summary>
    public bool FooterCompressShowsCancel => IsBusy && _footerCancelSlot == FooterCancelSlot.Compress;

    /// <summary>Backup slot interactive (run backup or cancel an in-flight backup).</summary>
    public bool BackupFooterEnabled => FooterBackupShowsCancel || (CanUseBackupAndCompress && !IsBusy);

    /// <summary>Compress slot interactive (run compress or cancel an in-flight compress).</summary>
    public bool CompressFooterEnabled => FooterCompressShowsCancel || (CanUseBackupAndCompress && !IsBusy);

    /// <summary>Scan, settings, tools, etc. ÔÇö disabled during backup/compress.</summary>
    public bool CanUseFooterCommands => !IsBusy;

    /// <summary>In-flight backup/compress ÔÇö Escape and footer Cancel.</summary>
    public bool CanCancelOperation => _operationCts is not null && IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBackupOrCompressFooter));
        OnPropertyChanged(nameof(CanUseFooterCommands));
        OnPropertyChanged(nameof(CanCancelOperation));
        OnPropertyChanged(nameof(FooterBackupShowsCancel));
        OnPropertyChanged(nameof(FooterCompressShowsCancel));
        OnPropertyChanged(nameof(BackupFooterEnabled));
        OnPropertyChanged(nameof(CompressFooterEnabled));
    }

    /// <summary>Cancels in-flight backup or compress (no-op if idle).</summary>
    public void CancelOperation()
    {
        try
        {
            _operationCts?.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    public SandboxLogHub SandboxLog => _sandboxLog;

    /// <summary>Optional hook for in-window toasts (assigned from <see cref="GSBT.WinUI.Views.MainPage"/> after load).</summary>
    public Action<AutoBackupTipPayload>? NotifyAutoBackupTip { get; set; }

    /// <summary>Tip shown when <see cref="NotifyAutoBackupTip"/> was not yet wired (startup race before <see cref="GSBT.WinUI.Views.MainPage"/> loads).</summary>
    private AutoBackupTipPayload? _pendingAutoBackupTip;

    [ObservableProperty]
    private bool _backupIntegrityStripVisible;

    [ObservableProperty]
    private string _backupIntegrityStripMessage = string.Empty;

    private void TryNotifyAutoBackupToast(string message) =>
        TryNotifyAutoBackupToast(message, AutoBackupToastChrome.None, null, BackupToastSeverity.Neutral);

    private void TryNotifyAutoBackupToast(
        string message,
        AutoBackupToastChrome chrome,
        string? messageSecondLine = null,
        BackupToastSeverity severity = BackupToastSeverity.Neutral)
    {
        var wantsEphemeral = chrome == AutoBackupToastChrome.None
            && _settings.Get("in_app_ephemeral_status_enabled", true);
        var wantsBackupChrome = chrome != AutoBackupToastChrome.None
            && _settings.Get("in_app_backup_warnings_enabled", true);
        var wantsInApp = wantsEphemeral || wantsBackupChrome;

        var payload = new AutoBackupTipPayload(message, chrome, messageSecondLine, severity);

        if (wantsInApp)
        {
            if (NotifyAutoBackupTip is { } tip)
            {
                tip.Invoke(payload);
            }
            else
            {
                _pendingAutoBackupTip = payload;
            }
        }

        if (_settings.Get("notifications_enabled", false))
        {
            var title = severity switch
            {
                BackupToastSeverity.Warning => "Backup attention",
                BackupToastSeverity.Error => "Backup issue",
                _ => App.IsSandboxSimulationChild ? $"{AppAboutInfo.AppName} (Simulation)" : AppAboutInfo.AppName
            };
            var body = string.IsNullOrWhiteSpace(messageSecondLine)
                ? message
                : $"{message}\n{messageSecondLine}";
            OsAppNotifications.TryShow(_settings, title, body, severity);
        }
    }

    /// <summary>Call from <see cref="GSBT.WinUI.Views.MainPage"/> after assigning <see cref="NotifyAutoBackupTip"/> so early integrity toasts are not lost.</summary>
    public void FlushPendingAutoBackupTip()
    {
        if (_pendingAutoBackupTip is not { } pending)
        {
            return;
        }

        var allow = pending.Chrome == AutoBackupToastChrome.None
            ? _settings.Get("in_app_ephemeral_status_enabled", true)
            : _settings.Get("in_app_backup_warnings_enabled", true);
        if (!allow)
        {
            _pendingAutoBackupTip = null;
            return;
        }

        if (NotifyAutoBackupTip is not { } tip)
        {
            return;
        }

        tip.Invoke(pending);
        _pendingAutoBackupTip = null;
    }

    /// <summary>Clears Last backup column emphasis (red integrity and yellow checkpoint drift).</summary>
    public void ClearLastBackupIntegrityWarnings(IReadOnlyList<GameRowViewModel>? rows = null)
    {
        IEnumerable<GameRowViewModel> seq = rows is { Count: > 0 }
            ? rows
            : Games;

        foreach (var g in seq)
        {
            g.LastBackupIntegrityWarning = false;
            g.LastBackupCheckpointWarning = false;
        }
    }

    public MainViewModel(
        SettingsStore settings,
        IGameDetector gameDetector,
        SandboxLogHub sandboxLog,
        ISandboxRuntimeOverrides overrides,
        CompressionActivityTracker compressionActivity)
    {
        _settings = settings;
        _sandboxLog = sandboxLog;
        _overrides = overrides;
        _compressionActivity = compressionActivity;
        ApplyReplayTeachingTipsOnNextLaunchIfNeeded();
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("MainViewModel must be constructed on the UI thread.");

        var simSessionDir = App.IsSandboxSimulationChild ? SimulationSessionContext.SessionDirectory : null;
        var catalogPath = string.IsNullOrWhiteSpace(simSessionDir)
            ? null
            : Path.Combine(simSessionDir, "game_save_data.json");
        _catalogManager = new SaveCatalogManager(
            catalogPath,
            legacyCatalogPath: null,
            skipInitialDiskLoad: false,
            importLegacyCatalogIfMissing: false);
        var bundled = Path.Combine(AppContext.BaseDirectory, "data", "ludusavi-save-manifest.json");
        var provider = new LudusaviManifestProvider(bundledManifestPath: File.Exists(bundled) ? bundled : null);
        var reg = new RegistrySaveResolver();
        _registryResolver = reg;
        _scanService = new ScanService(gameDetector, _catalogManager, provider, reg);

        _backupCoordinator = new GameBackupCoordinator();
        _autoBackup = new AutoBackupWatcherService(
            settings,
            _catalogManager,
            _dispatcher,
            _backupCoordinator,
            sandboxLog,
            OnAutoBackupSucceeded,
            notifyUser: TryNotifyAutoBackupToast);

        Games.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUseBackupAndCompress));
            OnPropertyChanged(nameof(CanBackupOrCompressFooter));
            OnPropertyChanged(nameof(BackupFooterEnabled));
            OnPropertyChanged(nameof(CompressFooterEnabled));
        };

        _overrides.Changed += (_, _) => EnqueueUi(() => OnPropertyChanged(nameof(BackupSizeEstimateEnabled)));

        StartWithEmptyGameListUi();

        UpdateFilterLabel();
        ReapplyFilterFull();
        _autoBackup.RestartMonitoringIfNeeded();

        _backupIntegrityCoordinator = new DefaultBackupIntegrityCoordinator(
            _settings,
            _dispatcher,
            ReconcileLastBackupDiskIntegrity);
        EnqueueUi(ReconcileLastBackupDiskIntegrity);

        if (App.IsSandboxSimulationChild)
        {
            // Child session uses an isolated backup tree; some hosts deliver few/no FileSystemWatcher callbacks
            // for that subtree. A light poll keeps Last backup yellow/red parity with the real app.
            var poll = _dispatcher.CreateTimer();
            poll.Interval = TimeSpan.FromSeconds(12);
            poll.IsRepeating = true;
            poll.Tick += SimulationBackupIntegrityPoll_Tick;
            poll.Start();
            _simulationBackupIntegrityPollTimer = poll;
        }

        _sandboxLog.Log("info", "Main window ready.");
        MigrateLegacyInAppDisplaySettingsIfNeeded();
    }

    /// <summary>Split legacy single toggle into ephemeral status vs backup-warning chrome.</summary>
    private void MigrateLegacyInAppDisplaySettingsIfNeeded()
    {
        const string legacy = "in_app_status_and_warnings_enabled";
        const string ephemeral = "in_app_ephemeral_status_enabled";
        const string warnings = "in_app_backup_warnings_enabled";
        if (_settings.ContainsKey(legacy) && !_settings.ContainsKey(ephemeral))
        {
            var v = _settings.Get(legacy, true);
            _settings.Set(ephemeral, v);
            _settings.Set(warnings, v);
        }
    }

    /// <summary>Persisted flag: user asked to reset all dismissible teaching tips on the next cold start.</summary>
    public const string ReplayTeachingTipsOnNextLaunchSettingKey = "replay_teaching_tips_on_next_launch";

    private static readonly string[] TeachingTipDismissalKeys =
    [
        "backup_bulk_teaching_tip_shown",
        "compress_teaching_tip_shown",
        "onboarding_scan_add_tip_shown",
        "settings_compression_7zip_engine_tip_shown",
        "settings_after_compress_pointer_tip_shown",
    ];

    /// <summary>If the user opted in, clear all teaching-tip dismissal flags once, then clear the opt-in so the UI shows unchecked next launch.</summary>
    private void ApplyReplayTeachingTipsOnNextLaunchIfNeeded()
    {
        if (!_settings.Get(ReplayTeachingTipsOnNextLaunchSettingKey, false))
        {
            return;
        }

        foreach (var key in TeachingTipDismissalKeys)
        {
            _settings.Set(key, false);
        }

        _settings.Set(ReplayTeachingTipsOnNextLaunchSettingKey, false);
        _pendingReplayCheckpointBulkBackup = true;
        _sandboxLog.Log("info", "Teaching tips were reset for this launch (replay-on-next-launch consumed).");
    }

    public bool HasPendingReplayCheckpointBulkBackup() => _pendingReplayCheckpointBulkBackup;

    public void MarkBackupTeachingTipDismissed() => _settings.Set("backup_bulk_teaching_tip_shown", true);

    public void MarkOnboardingScanAddTipDismissed() => _settings.Set("onboarding_scan_add_tip_shown", true);

    public void MarkCompressTeachingTipDismissed() => _settings.Set("compress_teaching_tip_shown", true);

    /// <summary>Footer bulk-backup teaching tip when the grid has rows and the tip has not been dismissed.</summary>
    public void TryInvokeBackupBulkTeachingTipIfDue()
    {
        if (Games.Count <= 0)
        {
            return;
        }

        if (IsTeachingTipMarkedShown("backup_bulk_teaching_tip_shown"))
        {
            _pendingReplayCheckpointBulkBackup = false;
            return;
        }

        _pendingReplayCheckpointBulkBackup = false;
        TeachingTipBackupBulkRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Honours simulated first-launch / real Settings for teaching-tip keys.</summary>
    private bool IsTeachingTipMarkedShown(string key) =>
        !_overrides.SimulateFirstAppLaunch && _settings.Get(key, false);

    /// <summary>First-launch empty grid: offer Scan / Add custom teaching tip once.</summary>
    public void RequestOnboardingScanAddTipIfNeeded()
    {
        if (IsTeachingTipMarkedShown("onboarding_scan_add_tip_shown"))
        {
            return;
        }

        if (Games.Count > 0)
        {
            return;
        }

        TeachingTipOnboardingScanAddRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool ShouldShowCompressTeachingTip() => !IsTeachingTipMarkedShown("compress_teaching_tip_shown");

    /// <summary>Settings ÔåÆ Compression one-time tip (always persisted; not tied to sandbox ÔÇ£first launchÔÇØ override).</summary>
    public bool ShouldShowCompressionSevenZipEngineTip() =>
        !_settings.Get("settings_compression_7zip_engine_tip_shown", false);

    public void MarkCompressionSevenZipEngineTipDismissed() =>
        _settings.Set("settings_compression_7zip_engine_tip_shown", true);

    /// <summary>Short subtitle for the Settings footer Compression tab teaching tip (preset only).</summary>
    public string CompressionSevenZipEngineTeachingTipIntro =>
        App.IsSandboxSimulationChild
            ? "Pick the 7-Zip engine above for smaller archives. Get 7-Zip is disabled in this simulation window."
            : "Pick the 7-Zip engine above for smaller, faster backups.";

    /// <summary>Shown once after the footer ÔÇ£Compress your backupsÔÇØ teaching tip is dismissed.</summary>
    public bool ShouldShowSettingsAfterCompressTeachingTip() =>
        !_settings.Get("settings_after_compress_pointer_tip_shown", false);

    public void MarkSettingsAfterCompressTeachingTipDismissed() =>
        _settings.Set("settings_after_compress_pointer_tip_shown", true);

    /// <summary>True when default or last backup path resolves to a folder we can write to (same rules as backup).</summary>
    public bool HasConfiguredBackupDestination()
    {
        if (_overrides.SimulateNoBackupDestination)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ResolveBackupDestination());
    }

    /// <summary>Save folder picked from the backup prompt; <paramref name="useAsDefault"/> mirrors Python &quot;use as default&quot; checkbox.</summary>
    public void PersistBackupDestinationFromPrompt(string path, bool useAsDefault)
    {
        var trimmed = path.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(trimmed);
        }
        catch
        {
            return;
        }

        if (useAsDefault)
        {
            _settings.Set("default_backup_path", trimmed);
        }
        else
        {
            _settings.Set("last_backup_path", trimmed);
        }

        _autoBackup.RestartMonitoringIfNeeded();
        ReconcileLastBackupDiskIntegrity();
    }

    public void Shutdown()
    {
        if (_simulationBackupIntegrityPollTimer is not null)
        {
            _simulationBackupIntegrityPollTimer.Stop();
            _simulationBackupIntegrityPollTimer.Tick -= SimulationBackupIntegrityPoll_Tick;
            _simulationBackupIntegrityPollTimer = null;
        }

        _backupIntegrityCoordinator.Dispose();
        _autoBackup.Dispose();
    }


    private void EnqueueUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(new Microsoft.UI.Dispatching.DispatcherQueueHandler(() => action()));
        }
    }
}
