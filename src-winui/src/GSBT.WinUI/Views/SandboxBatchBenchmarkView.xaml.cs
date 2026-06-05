using GSBT.Core.Services;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI;

namespace GSBT.WinUI.Views;

/// <summary>Batch compression queue hosted inside Sandbox Monitor (sidebar).</summary>
public sealed partial class SandboxBatchBenchmarkView : UserControl
{
    private const int MaxBatchRows = 12;
    private const int DefaultBatchRows = 3;
    private const double IconButtonSize = 22;
    private const double RowEditorMinHeight = 36;
    private const double TestTitleFontSize = 14.5;
    private static readonly TimeSpan CompletedProgressHold = TimeSpan.FromSeconds(5);

    private readonly MainViewModel _vm;
    private readonly SandboxCompressionBenchmarkStore _store;
    private readonly SandboxLogHub _log;
    private readonly SettingsStore _settings;
    private readonly SandboxMonitorSession _monitorSession;
    private readonly SandboxBatchPerformanceHub _batchPerfHub;
    private readonly SandboxResourceMonitor _resourceMonitor;
    private readonly CompressionActivityTracker _compressionActivity;
    private readonly Func<Task> _onRecordedAsync;
    private readonly BackupCompressionService _compression = new();
    private readonly List<BatchRowHost> _batchRows = new();
    private readonly SolidColorBrush _cancelEnabledBackground = new(Color.FromArgb(255, 0xC4, 0x2B, 0x1C));
    private readonly SolidColorBrush _cancelEnabledForeground = new(Color.FromArgb(255, 255, 255, 255));
    private long _requestedThemePropertyCallbackToken;
    private CancellationTokenSource? _batchCts;
    private bool _batchRunning;

    public SandboxBatchBenchmarkView(
        MainViewModel vm,
        SandboxCompressionBenchmarkStore store,
        SandboxLogHub log,
        SettingsStore settings,
        SandboxMonitorSession monitorSession,
        SandboxBatchPerformanceHub batchPerfHub,
        SandboxResourceMonitor resourceMonitor,
        CompressionActivityTracker compressionActivity,
        Func<Task> onRecordedAsync)
    {
        _vm = vm;
        _store = store;
        _log = log;
        _settings = settings;
        _monitorSession = monitorSession;
        _batchPerfHub = batchPerfHub;
        _resourceMonitor = resourceMonitor;
        _compressionActivity = compressionActivity;
        _onRecordedAsync = onRecordedAsync;
        InitializeComponent();

        for (var i = 0; i < DefaultBatchRows; i++)
        {
            AddBatchRowCore();
        }

        _requestedThemePropertyCallbackToken = RegisterPropertyChangedCallback(
            RequestedThemeProperty,
            (_, _) => RefreshBatchRowChromeBrushes());
        ActualThemeChanged += SandboxBatchBenchmarkView_ActualThemeChanged;
        Loaded += SandboxBatchBenchmarkView_Loaded;

        UpdateAddRowButton();
        UpdateRunCancelUi();
        Unloaded += SandboxBatchBenchmarkView_Unloaded;
        KeyDown += SandboxBatchBenchmarkView_KeyDown;
    }

    private void SandboxBatchBenchmarkView_Loaded(object sender, RoutedEventArgs e) =>
        RefreshBatchRowChromeBrushes();

    private void SandboxBatchBenchmarkView_ActualThemeChanged(FrameworkElement sender, object args) =>
        RefreshBatchRowChromeBrushes();

    private void SandboxBatchBenchmarkView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || !_batchRunning)
        {
            return;
        }

        _batchCts?.Cancel();
        StatusText.Text = "Cancelling…";
        e.Handled = true;
    }

    private void SandboxBatchBenchmarkView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Removed from ShellContent when switching monitor tabs; batch keeps running in the background.
        // Do not cancel CTS or unregister theme listeners here.
    }

    /// <summary>Cancel an in-flight batch (explicit user action or monitor window close).</summary>
    public void RequestCancelBatch()
    {
        if (!_batchRunning)
        {
            return;
        }

        _batchCts?.Cancel();
        StatusText.Text = "Cancelling…";
    }

    private void CancelAllRowProgressHolds()
    {
        foreach (var h in _batchRows)
        {
            h.CancelProgressHold();
        }
    }

    private void ResetAllProgressUi()
    {
        foreach (var h in _batchRows)
        {
            h.CancelProgressHold();
            h.StepProgress.Value = 0;
            h.StepProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void PrepareRowForActiveStep(int stepIndex)
    {
        for (var j = 0; j < stepIndex; j++)
        {
            var hj = _batchRows[j];
            hj.CancelProgressHold();
            hj.StepProgress.Value = 0;
            hj.StepProgress.Visibility = Visibility.Collapsed;
        }

        var active = _batchRows[stepIndex];
        active.CancelProgressHold();
        active.StepProgress.Visibility = Visibility.Visible;
        active.StepProgress.Value = 0;
    }

    private void ScheduleRowProgressHideAfterHold(BatchRowHost row)
    {
        row.CancelProgressHold();
        row.ProgressHoldCts = new CancellationTokenSource();
        var token = row.ProgressHoldCts.Token;
        _ = DelayRowProgressHideAsync(row, token);
    }

    private async Task DelayRowProgressHideAsync(BatchRowHost row, CancellationToken ct)
    {
        try
        {
            await Task.Delay(CompletedProgressHold, ct).ConfigureAwait(false);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                row.StepProgress.Value = 0;
                row.StepProgress.Visibility = Visibility.Collapsed;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanRun() =>
        !_vm.IsBusy
        && !_vm.IsScanning
        && _vm.GetEffectiveBackupRootForCompressPrompt() is { } p
        && Directory.Exists(p);

    private void UpdateAddRowButton() =>
        AddRowButton.IsEnabled = !_batchRunning && _batchRows.Count < MaxBatchRows;

    private void UpdateRunCancelUi()
    {
        RunBatchButton.IsEnabled = CanRun() && !_batchRunning;
        CancelBatchButton.IsEnabled = _batchRunning;
        ApplyCancelBatchButtonChrome();
    }

    private void ApplyCancelBatchButtonChrome()
    {
        if (_batchRunning)
        {
            CancelBatchButton.Background = _cancelEnabledBackground;
            CancelBatchButton.Foreground = _cancelEnabledForeground;
        }
        else
        {
            CancelBatchButton.ClearValue(Control.BackgroundProperty);
            CancelBatchButton.ClearValue(Control.ForegroundProperty);
        }
    }

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRows.Count >= MaxBatchRows)
        {
            return;
        }

        AddBatchRowCore();
        UpdateAddRowButton();
    }

    /// <summary>
    /// Match shell rules: explicit <see cref="RequestedTheme"/> wins over <see cref="ActualTheme"/>, which can lag a frame during live toggles.
    /// </summary>
    private bool IsSandboxPanelDarkChrome() =>
        RequestedTheme switch
        {
            ElementTheme.Dark => true,
            ElementTheme.Light => false,
            _ => ActualTheme == ElementTheme.Dark,
        };

    /// <summary>Code-built row cards use frozen brushes; re-apply when the monitor shell theme changes.</summary>
    private void RefreshBatchRowChromeBrushes()
    {
        var dark = IsSandboxPanelDarkChrome();
        foreach (var h in _batchRows)
        {
            h.RowBorder.Background = ThemeBridge.GetGsbtBrush(dark, "GsbtCardBgBrush");
            h.RowBorder.BorderBrush = ThemeBridge.GetGsbtBrush(dark, "GsbtBorderBrush");
            h.Label.Foreground = ThemeBridge.GetGsbtBrush(dark, "GsbtBodyTextBrush");
        }
    }

    private void AddBatchRowCore()
    {
        var host = new BatchRowHost();
        var dark = IsSandboxPanelDarkChrome();
        var n = _batchRows.Count + 1;
        host.RemoveButton = new Button
        {
            MinWidth = IconButtonSize,
            MinHeight = IconButtonSize,
            MaxWidth = IconButtonSize,
            MaxHeight = IconButtonSize,
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Top,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
        };
        AutomationProperties.SetName(host.RemoveButton, "Remove test row");
        ToolTipService.SetToolTip(host.RemoveButton, "Remove this row");

        host.RemoveButton.Click += (_, _) =>
        {
            if (_batchRows.Count <= 1)
            {
                return;
            }

            BatchRowsPanel.Children.Remove(host.RowBorder);
            host.CancelProgressHold();
            _batchRows.Remove(host);
            RelabelBatchRows();
            UpdateRowRemoveStates();
            UpdateAddRowButton();
        };

        host.Label = new TextBlock
        {
            Text = $"Test {n}",
            FontWeight = FontWeights.Bold,
            FontSize = TestTitleFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBridge.GetGsbtBrush(dark, "GsbtBodyTextBrush"),
        };
        host.Label.DoubleTapped += async (_, _) => await PromptRenameRowAsync(host);

        host.RenameButton = new Button
        {
            Width = 20,
            Height = 20,
            MinWidth = 20,
            MinHeight = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE104", FontSize = 11 },
        };
        AutomationProperties.SetName(host.RenameButton, "Rename test");
        ToolTipService.SetToolTip(host.RenameButton, "Rename test");
        host.RenameButton.Click += async (_, _) => await PromptRenameRowAsync(host);

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(host.Label);
        titleRow.Children.Add(host.RenameButton);

        host.StepProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetName(host.StepProgress, "Test progress");

        var header = new Grid { VerticalAlignment = VerticalAlignment.Center };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleRow, 0);
        Grid.SetColumn(host.RemoveButton, 1);
        header.Children.Add(titleRow);
        header.Children.Add(host.RemoveButton);

        host.Preset = CreatePresetCombo();
        host.Format = CreateFormatCombo();
        host.Mx = new NumberBox
        {
            Minimum = 0,
            Maximum = 9,
            Value = 5,
            Header = "-mx (7-Zip)",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinHeight = RowEditorMinHeight,
            MinWidth = 120,
        };
        host.Threads = new NumberBox
        {
            Minimum = 0,
            Maximum = 128,
            Value = 0,
            Header = "-mmt (0=auto)",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinHeight = RowEditorMinHeight,
            MinWidth = 130,
        };

        host.Preset.SelectionChanged += (_, _) => SyncRowSevenZipUi(host);

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(header);
        inner.Children.Add(host.Preset);
        inner.Children.Add(
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { host.Format, host.Mx, host.Threads },
            });
        inner.Children.Add(host.StepProgress);

        host.RowBorder = new Border
        {
            BorderBrush = ThemeBridge.GetGsbtBrush(dark, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Background = ThemeBridge.GetGsbtBrush(dark, "GsbtCardBgBrush"),
            Child = inner,
        };

        _batchRows.Add(host);
        BatchRowsPanel.Children.Add(host.RowBorder);
        SyncRowSevenZipUi(host);
        RelabelBatchRows();
        UpdateRowRemoveStates();
        UpdateAddRowButton();
    }

    private void RelabelBatchRows()
    {
        for (var i = 0; i < _batchRows.Count; i++)
        {
            RefreshRowTitle(_batchRows[i], i);
        }
    }

    private static void RefreshRowTitle(BatchRowHost host, int index)
    {
        host.Label.Text = string.IsNullOrEmpty(host.CustomName)
            ? $"Test {index + 1}"
            : host.CustomName;
    }

    private async Task PromptRenameRowAsync(BatchRowHost host)
    {
        if (_batchRunning || XamlRoot is null)
        {
            return;
        }

        var index = _batchRows.IndexOf(host);
        if (index < 0)
        {
            return;
        }

        var input = new TextBox
        {
            Text = host.Label.Text,
            MaxLength = BatchTestDisplayName.MaxInputLength,
            PlaceholderText = $"Test {index + 1}",
        };
        var dlg = new ContentDialog
        {
            Title = "Rename test",
            Content = input,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        if (await GsbtContentDialog.ShowAsync(dlg).ConfigureAwait(true) != ContentDialogResult.Primary)
        {
            return;
        }

        var resolved = BatchTestDisplayName.Resolve(input.Text, index);
        var defaultTitle = $"Test {index + 1}";
        host.CustomName = string.Equals(resolved, defaultTitle, StringComparison.Ordinal)
            ? null
            : resolved;
        RefreshRowTitle(host, index);
    }

    private void UpdateRowRemoveStates()
    {
        var canRemove = _batchRows.Count > 1;
        foreach (var h in _batchRows)
        {
            h.RemoveButton.IsEnabled = canRemove && !_batchRunning;
            h.RenameButton.IsEnabled = !_batchRunning;
        }
    }

    private static ComboBox CreatePresetCombo()
    {
        var c = new GsbtComboBox
        {
            MinHeight = RowEditorMinHeight,
            MinWidth = 280,
            Header = "Engine preset",
        };
        c.Items.Add(new ComboBoxItem { Content = "Store (ZIP, no compression)", Tag = CompressionOptionsResolver.PresetStore });
        c.Items.Add(new ComboBoxItem { Content = "ZIP — fast deflate", Tag = CompressionOptionsResolver.PresetDeflateFast });
        c.Items.Add(new ComboBoxItem { Content = "ZIP — balanced deflate", Tag = CompressionOptionsResolver.PresetDeflateBalanced });
        c.Items.Add(new ComboBoxItem { Content = "ZIP — max deflate", Tag = CompressionOptionsResolver.PresetDeflateMax });
        c.Items.Add(new ComboBoxItem { Content = "7-Zip engine", Tag = CompressionOptionsResolver.PresetSevenZip });
        c.SelectedIndex = 2;
        return c;
    }

    private static ComboBox CreateFormatCombo()
    {
        var c = new GsbtComboBox
        {
            MinHeight = RowEditorMinHeight,
            MinWidth = 200,
            Header = "7-Zip output",
        };
        c.Items.Add(new ComboBoxItem { Content = ".7z (LZMA2)", Tag = "7z" });
        c.Items.Add(new ComboBoxItem { Content = ".zip (Deflate via 7z)", Tag = "zip" });
        c.SelectedIndex = 0;
        return c;
    }

    private static void SyncRowSevenZipUi(BatchRowHost h)
    {
        var is7 = (h.Preset.SelectedItem as ComboBoxItem)?.Tag as string == CompressionOptionsResolver.PresetSevenZip;
        h.Format.IsEnabled = is7;
        h.Mx.IsEnabled = is7;
        h.Threads.IsEnabled = is7;
    }

    private async void RunBatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRun() || _batchRunning)
        {
            StatusText.Text = "Cannot run: wait until the main app is idle and a valid backup folder is set.";
            return;
        }

        var backup = _vm.GetEffectiveBackupRootForCompressPrompt()!;
        var sevenPathSetting = _settings.Get("compression_7z_path", string.Empty) ?? string.Empty;
        var specs = new List<BatchTestBeginSpec>();
        foreach (var h in _batchRows)
        {
            var preset = (h.Preset.SelectedItem as ComboBoxItem)?.Tag as string ?? CompressionOptionsResolver.PresetDeflateBalanced;
            var fmt = (h.Format.SelectedItem as ComboBoxItem)?.Tag as string ?? "7z";
            var name = string.IsNullOrEmpty(h.CustomName) ? null : h.CustomName;
            specs.Add(new BatchTestBeginSpec(
                CompressionOptionsResolver.NormalizePreset(preset),
                CompressionOptionsResolver.Normalize7zFormat(fmt),
                (int)Math.Clamp(h.Mx.Value, 0, 9),
                (int)Math.Clamp(h.Threads.Value, 0, 128),
                name));
        }

        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        _batchRunning = true;
        _monitorSession.SetBatchBenchmarkRunning(true);
        UpdateRunCancelUi();
        UpdateRowRemoveStates();
        AddRowButton.IsEnabled = false;
        ResetAllProgressUi();
        _resourceMonitor.ClearCheckpoints();
        _resourceMonitor.BeginBatchHistory();
        _batchPerfHub.BeginBatch(specs);

        var dq = DispatcherQueue;
        var cancelled = false;

        try
        {
            for (var i = 0; i < specs.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                PrepareRowForActiveStep(i);
                var step = specs[i];
                var preset = step.Preset;
                var fmt = step.Format;
                var mx = step.Mx;
                var threads = step.Threads;
                var rowHost = _batchRows[i];
                _batchPerfHub.SetStepRunning(i);
                var checkpointLabel = BatchTestDisplayName.TruncateForCheckpoint(
                    BatchTestDisplayName.Resolve(rowHost.CustomName, i));
                var paramLine = BatchTestParameterFormatter.BuildCompact(preset, fmt, mx, threads);
                _resourceMonitor.NotifyBatchStepStarting(i, checkpointLabel, paramLine);
                StatusText.Text = $"Batch step {i + 1} of {specs.Count}…";
                _log.Log("benchmark", $"Batch {i + 1}/{specs.Count}: preset={preset} format={fmt} -mx={mx} -mmt={threads}");
                var opts = CompressionOptionsResolver.FromExplicit(preset, fmt, mx, threads, sevenPathSetting);
                var progress = new Progress<int>(pct =>
                {
                    var p = Math.Clamp(pct, 0, 100);
                    _ = dq.TryEnqueue(() =>
                    {
                        rowHost.StepProgress.Value = p;
                        _batchPerfHub.SetStepProgress(i, p);
                    });
                });
                BackupCompressionResult result;
                try
                {
                    _monitorSession.SetCompressionWorkloadActive(true);
                    _compressionActivity.Clear();
                    result = await Task.Run(
                            async () =>
                                await _compression.CompressBackupFolderAsync(
                                        backup,
                                        opts,
                                        progress,
                                        m => _log.Log("compress", m),
                                        folder => _compressionActivity.SetCurrentGameFolder(folder),
                                        token)
                                    .ConfigureAwait(false))
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    StatusText.Text = "Batch cancelled.";
                    _log.Log("benchmark", "Batch cancelled by user.");
                    _ = dq.TryEnqueue(ResetAllProgressUi);
                    return;
                }
                catch (Exception ex)
                {
                    _batchPerfHub.SetStepFailed(i);
                    StatusText.Text = $"Batch stopped at step {i + 1}: {ex.Message}";
                    _log.Log("benchmark", $"Batch step {i + 1} exception: {ex.Message}");
                    _ = dq.TryEnqueue(ResetAllProgressUi);
                    return;
                }
                finally
                {
                    _monitorSession.SetCompressionWorkloadActive(false);
                    _compressionActivity.Clear();
                }

                var serial = _resourceMonitor.NotifyBatchStepEnded(i);

                var entry = SandboxBenchmarkFormat.FromResult(backup, result);
                _batchPerfHub.SetStepCompleted(i, serial, entry);

                _ = dq.TryEnqueue(() => rowHost.StepProgress.Value = 100);
                ScheduleRowProgressHideAfterHold(rowHost);

                await _store.AppendAsync(entry).ConfigureAwait(true);
                _log.Log("benchmark", $"Batch step {i + 1} recorded: {entry.TitleLine}");
                StatusText.Text = $"Batch {i + 1}/{specs.Count} done — {entry.TitleLine}";
                await _onRecordedAsync().ConfigureAwait(true);
            }

            StatusText.Text = $"Batch finished ({specs.Count} step(s)).";
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            StatusText.Text = "Batch cancelled.";
            _log.Log("benchmark", "Batch cancelled.");
            CancelAllRowProgressHolds();
            ResetAllProgressUi();
        }
        finally
        {
            _batchPerfHub.EndBatch(cancelled);
            _batchRunning = false;
            _monitorSession.SetBatchBenchmarkRunning(false);
            _batchCts?.Dispose();
            _batchCts = null;
            UpdateRunCancelUi();
            UpdateRowRemoveStates();
            UpdateAddRowButton();
        }
    }

    private void CancelBatchButton_Click(object sender, RoutedEventArgs e)
    {
        _batchCts?.Cancel();
        StatusText.Text = "Cancelling…";
    }

    private sealed class BatchRowHost
    {
        public TextBlock Label { get; set; } = null!;
        public Button RenameButton { get; set; } = null!;
        public string? CustomName { get; set; }
        public Button RemoveButton { get; set; } = null!;
        public ComboBox Preset { get; set; } = null!;
        public ComboBox Format { get; set; } = null!;
        public NumberBox Mx { get; set; } = null!;
        public NumberBox Threads { get; set; } = null!;
        public ProgressBar StepProgress { get; set; } = null!;
        public Border RowBorder { get; set; } = null!;
        public CancellationTokenSource? ProgressHoldCts { get; set; }

        public void CancelProgressHold()
        {
            ProgressHoldCts?.Cancel();
            ProgressHoldCts?.Dispose();
            ProgressHoldCts = null;
        }
    }
}
