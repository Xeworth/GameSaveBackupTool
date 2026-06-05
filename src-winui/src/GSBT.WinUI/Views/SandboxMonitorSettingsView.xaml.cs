using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxMonitorSettingsView : UserControl
{
    private readonly SettingsStore _store;
    private readonly SandboxMonitorSession _session;
    private readonly SandboxLogHub _log;
    private Microsoft.UI.Xaml.DispatcherTimer? _savedTimer;
    private Microsoft.UI.Xaml.DispatcherTimer? _sessionPollTimer;
    private bool _suppressChangeHandlers;

    public SandboxMonitorSettingsView(SettingsStore store, SandboxMonitorSession session, SandboxLogHub log)
    {
        _store = store;
        _session = session;
        _log = log;
        InitializeComponent();

        LogDetailCombo.Items.Add(new ComboBoxItem { Content = "Quiet (warnings + benchmark + compress summaries)", Tag = "quiet" });
        LogDetailCombo.Items.Add(new ComboBoxItem { Content = "Normal (use category switches)", Tag = "normal" });
        LogDetailCombo.Items.Add(new ComboBoxItem { Content = "Verbose (everything)", Tag = "verbose" });

        SyncCloseMainCheck.Checked += OnWindowSettingChanged;
        SyncCloseMainCheck.Unchecked += OnWindowSettingChanged;
        SyncCloseMainSkipCheck.Checked += OnWindowSettingChanged;
        SyncCloseMainSkipCheck.Unchecked += OnWindowSettingChanged;
        CloseMonitorWhenMainCheck.Checked += OnWindowSettingChanged;
        CloseMonitorWhenMainCheck.Unchecked += OnWindowSettingChanged;
        CloseMonitorWhenMainSkipCheck.Checked += OnWindowSettingChanged;
        CloseMonitorWhenMainSkipCheck.Unchecked += OnWindowSettingChanged;

        LogDetailCombo.SelectionChanged += OnLogSettingChanged;
        LogShowScanCheck.Checked += OnLogSettingChanged;
        LogShowScanCheck.Unchecked += OnLogSettingChanged;
        LogShowCompressCheck.Checked += OnLogSettingChanged;
        LogShowCompressCheck.Unchecked += OnLogSettingChanged;
        LogShowCompressTicksCheck.Checked += OnLogSettingChanged;
        LogShowCompressTicksCheck.Unchecked += OnLogSettingChanged;
        LogShowBenchmarkCheck.Checked += OnLogSettingChanged;
        LogShowBenchmarkCheck.Unchecked += OnLogSettingChanged;
        LogShowInfoCheck.Checked += OnLogSettingChanged;
        LogShowInfoCheck.Unchecked += OnLogSettingChanged;
        LogShowWarnCheck.Checked += OnLogSettingChanged;
        LogShowWarnCheck.Unchecked += OnLogSettingChanged;
        LogShow7zipCheck.Checked += OnLogSettingChanged;
        LogShow7zipCheck.Unchecked += OnLogSettingChanged;
        LogMaxTailLinesBox.ValueChanged += LogMaxTailLinesBox_OnValueChanged;

        PerfRecordWhenIdleCheck.Checked += OnPerfSettingChanged;
        PerfRecordWhenIdleCheck.Unchecked += OnPerfSettingChanged;
        PerfShowGsbtCheck.Checked += OnPerfSettingChanged;
        PerfShowGsbtCheck.Unchecked += OnPerfSettingChanged;
        PerfShowCompressionCheck.Checked += OnPerfSettingChanged;
        PerfShowCompressionCheck.Unchecked += OnPerfSettingChanged;

        Loaded += SandboxMonitorSettingsView_Loaded;
        Unloaded += SandboxMonitorSettingsView_Unloaded;
    }

    private void SandboxMonitorSettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWindowSettingsFromStore();
        RefreshLogSettingsFromStore();
        RefreshPerformanceSettingsFromStore();
        if (_sessionPollTimer is null)
        {
            _sessionPollTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _sessionPollTimer.Tick += SessionPollTimer_Tick;
            _sessionPollTimer.Start();
        }
    }

    private void SandboxMonitorSettingsView_Unloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SandboxMonitorSettingsView_Loaded;
        Unloaded -= SandboxMonitorSettingsView_Unloaded;
        if (_sessionPollTimer is not null)
        {
            _sessionPollTimer.Stop();
            _sessionPollTimer.Tick -= SessionPollTimer_Tick;
            _sessionPollTimer = null;
        }

        _savedTimer?.Stop();
        _savedTimer = null;
    }

    private void SessionPollTimer_Tick(object? sender, object e)
    {
        BatchRunningHint.Visibility = _session.IsBatchBenchmarkRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWindowSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        SyncWindowDependentUi();
        PersistWindowSettings();
    }

    private void OnPerfSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        PersistPerformanceSettings();
    }

    private void OnLogSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        SyncLogDependentUi();
        PersistLogSettings();
    }

    private void LogMaxTailLinesBox_OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressChangeHandlers)
        {
            return;
        }

        PersistLogSettings();
    }

    private void ClearLogBufferButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        FlashSaved();
    }

    private void RefreshWindowSettingsFromStore()
    {
        _suppressChangeHandlers = true;
        try
        {
            SyncCloseMainCheck.IsChecked = _store.Get("sandbox_monitor_sync_close", false);
            SyncCloseMainSkipCheck.IsChecked = _store.Get("sandbox_monitor_sync_close_skip_confirm", false);
            CloseMonitorWhenMainCheck.IsChecked = _store.Get("sandbox_close_monitor_when_main_closes", false);
            CloseMonitorWhenMainSkipCheck.IsChecked = _store.Get("sandbox_close_monitor_when_main_closes_skip_confirm", false);
            SyncWindowDependentUi();
        }
        finally
        {
            _suppressChangeHandlers = false;
        }
    }

    private void RefreshPerformanceSettingsFromStore()
    {
        _suppressChangeHandlers = true;
        try
        {
            PerfRecordWhenIdleCheck.IsChecked = _store.Get(SandboxResourceMonitor.RecordWhenIdleSettingsKey, false);
            PerfShowGsbtCheck.IsChecked = PerformanceChartDisplaySettings.ShowGsbt(_store);
            PerfShowCompressionCheck.IsChecked = PerformanceChartDisplaySettings.ShowCompression(_store);
        }
        finally
        {
            _suppressChangeHandlers = false;
        }
    }

    private void RefreshLogSettingsFromStore()
    {
        _suppressChangeHandlers = true;
        try
        {
            var detail = (_store.Get("sandbox_log_detail", "normal") ?? "normal").Trim().ToLowerInvariant();
            LogDetailCombo.SelectedItem = LogDetailCombo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => string.Equals(i.Tag as string, detail, StringComparison.OrdinalIgnoreCase))
                ?? LogDetailCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == "normal");

            LogShowScanCheck.IsChecked = _store.Get("sandbox_log_show_scan", true);
            LogShowCompressCheck.IsChecked = _store.Get("sandbox_log_show_compress", true);
            LogShowCompressTicksCheck.IsChecked = _store.Get("sandbox_log_show_compress_ticks", true);
            LogShowBenchmarkCheck.IsChecked = _store.Get("sandbox_log_show_benchmark", true);
            LogShowInfoCheck.IsChecked = _store.Get("sandbox_log_show_info", true);
            LogShowWarnCheck.IsChecked = _store.Get("sandbox_log_show_warn", true);
            LogShow7zipCheck.IsChecked = _store.Get("sandbox_log_show_7zip", true);
            LogMaxTailLinesBox.Value = Math.Clamp(_store.Get("sandbox_log_max_tail_lines", 5000), 500, 20000);
            SyncLogDependentUi();
        }
        finally
        {
            _suppressChangeHandlers = false;
        }
    }

    private void SyncWindowDependentUi()
    {
        var syncMain = SyncCloseMainCheck.IsChecked == true;
        SyncCloseMainSkipCheck.IsEnabled = syncMain;
        if (!syncMain)
        {
            _suppressChangeHandlers = true;
            try
            {
                SyncCloseMainSkipCheck.IsChecked = false;
            }
            finally
            {
                _suppressChangeHandlers = false;
            }
        }

        var closeMon = CloseMonitorWhenMainCheck.IsChecked == true;
        CloseMonitorWhenMainSkipCheck.IsEnabled = closeMon;
        if (!closeMon)
        {
            _suppressChangeHandlers = true;
            try
            {
                CloseMonitorWhenMainSkipCheck.IsChecked = false;
            }
            finally
            {
                _suppressChangeHandlers = false;
            }
        }
    }

    private void SyncLogDependentUi()
    {
        var detail = (LogDetailCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "normal";
        var normal = string.Equals(detail, "normal", StringComparison.OrdinalIgnoreCase);
        LogShowScanCheck.IsEnabled = normal;
        LogShowCompressCheck.IsEnabled = normal;
        LogShowCompressTicksCheck.IsEnabled = normal && LogShowCompressCheck.IsChecked == true;
        LogShowBenchmarkCheck.IsEnabled = normal;
        LogShowInfoCheck.IsEnabled = normal;
        LogShowWarnCheck.IsEnabled = normal;
        LogShow7zipCheck.IsEnabled = normal;
        LogMaxTailLinesBox.IsEnabled = true;
    }

    private void PersistWindowSettings()
    {
        _store.Set("sandbox_monitor_sync_close", SyncCloseMainCheck.IsChecked == true);
        _store.Set("sandbox_monitor_sync_close_skip_confirm", SyncCloseMainSkipCheck.IsChecked == true);
        _store.Set("sandbox_close_monitor_when_main_closes", CloseMonitorWhenMainCheck.IsChecked == true);
        _store.Set("sandbox_close_monitor_when_main_closes_skip_confirm", CloseMonitorWhenMainSkipCheck.IsChecked == true);
        FlashSaved();
    }

    private void PersistPerformanceSettings()
    {
        _store.Set(SandboxResourceMonitor.RecordWhenIdleSettingsKey, PerfRecordWhenIdleCheck.IsChecked == true);
        _store.Set(PerformanceChartDisplaySettings.ShowGsbtKey, PerfShowGsbtCheck.IsChecked == true);
        _store.Set(PerformanceChartDisplaySettings.ShowCompressionKey, PerfShowCompressionCheck.IsChecked == true);
        FlashSaved();
    }

    private void PersistLogSettings()
    {
        var detail = (LogDetailCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "normal";
        _store.Set("sandbox_log_detail", detail);
        _store.Set("sandbox_log_show_scan", LogShowScanCheck.IsChecked == true);
        _store.Set("sandbox_log_show_compress", LogShowCompressCheck.IsChecked == true);
        _store.Set("sandbox_log_show_compress_ticks", LogShowCompressTicksCheck.IsChecked == true);
        _store.Set("sandbox_log_show_benchmark", LogShowBenchmarkCheck.IsChecked == true);
        _store.Set("sandbox_log_show_info", LogShowInfoCheck.IsChecked == true);
        _store.Set("sandbox_log_show_warn", LogShowWarnCheck.IsChecked == true);
        _store.Set("sandbox_log_show_7zip", LogShow7zipCheck.IsChecked == true);
        _store.Set("sandbox_log_max_tail_lines", (int)Math.Clamp(LogMaxTailLinesBox.Value, 500, 20000));
        _log.NotifyPreferencesChanged();
        FlashSaved();
    }

    private void FlashSaved()
    {
        SavedHint.Visibility = Visibility.Visible;
        _savedTimer?.Stop();
        _savedTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _savedTimer.Tick += (_, _) =>
        {
            SavedHint.Visibility = Visibility.Collapsed;
            _savedTimer?.Stop();
            _savedTimer = null;
        };
        _savedTimer.Start();
    }
}
