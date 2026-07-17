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

        host.ModeCombo = new ComboBox
        {
            Header = "Compression type",
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };
        host.ModeCombo.Items.Add(new ComboBoxItem { Content = "Chunky", Tag = "solid" });
        host.ModeCombo.Items.Add(new ComboBoxItem { Content = "Smooth", Tag = "smooth" });
        host.ModeCombo.SelectedIndex = 0;
        ToolTipService.SetToolTip(
            host.ModeCombo,
            "Chunky uses solid archives for smaller output. Smooth uses per-file packing for steadier progress and quicker cancel.");

        var headerLeft = new StackPanel { Spacing = 2 };
        headerLeft.Children.Add(titleRow);
        headerLeft.Children.Add(host.ModeCombo);

        var header = new Grid { VerticalAlignment = VerticalAlignment.Center };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerLeft, 0);
        Grid.SetColumn(host.RemoveButton, 1);
        header.Children.Add(headerLeft);
        header.Children.Add(host.RemoveButton);

        var mxIndexMax = SevenZipCompressionLevelMapper.SliderIndexCount - 1;
        host.LevelSlider = new Slider
        {
            Minimum = 0,
            Maximum = mxIndexMax,
            StepFrequency = 1,
            TickFrequency = 1,
            TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside,
            Value = SevenZipCompressionLevelMapper.SliderIndexFromMx(5),
            Header = "Compression level",
        };
        CompressionLevelSliderUi.WireMxLevelFlyout(host.LevelSlider);

        var threadMax = CompressionOptionsResolver.LogicalProcessorCount;
        host.ThreadSlider = new Slider
        {
            Minimum = 0,
            Maximum = threadMax,
            StepFrequency = 1,
            TickFrequency = 1,
            TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside,
            Value = 0,
            Header = "CPU threads",
        };
        host.ThreadLabel = new TextBlock
        {
            Text = "Auto",
            FontSize = 12,
            MinWidth = 36,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBridge.GetGsbtBrush(dark, "GsbtBodyTextBrush"),
        };
        host.ThreadSlider.ValueChanged += (_, _) =>
        {
            var t = (int)Math.Round(host.ThreadSlider.Value);
            host.ThreadLabel.Text = t <= 0 ? "Auto" : t.ToString();
        };

        var threadRow = new Grid { ColumnSpacing = 10 };
        threadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        threadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(host.ThreadSlider, 0);
        Grid.SetColumn(host.ThreadLabel, 1);
        threadRow.Children.Add(host.ThreadSlider);
        threadRow.Children.Add(host.ThreadLabel);

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(header);
        inner.Children.Add(host.LevelSlider);
        inner.Children.Add(threadRow);
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

    private async void RunBatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRun() || _batchRunning)
        {
            StatusText.Text = "Cannot run: wait until the main app is idle and a valid backup folder is set.";
            return;
        }

        var backup = _vm.GetEffectiveBackupRootForCompressPrompt()!;
        var specs = new List<BatchTestBeginSpec>();
        foreach (var h in _batchRows)
        {
            var mx = SevenZipCompressionLevelMapper.MxFromSliderIndex((int)Math.Round(h.LevelSlider.Value));
            var threads = CompressionOptionsResolver.NormalizeThreadCount(
                (int)Math.Round(h.ThreadSlider.Value),
                CompressionOptionsResolver.LogicalProcessorCount);
            var solidArchive = IsSolidArchive(h);
            var name = string.IsNullOrEmpty(h.CustomName) ? null : h.CustomName;
            specs.Add(new BatchTestBeginSpec(mx, threads, solidArchive, name));
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
                var mx = step.Mx;
                var threads = step.Threads;
                var solidArchive = step.SolidArchive;
                var rowHost = _batchRows[i];
                _batchPerfHub.SetStepRunning(i);
                var checkpointLabel = BatchTestDisplayName.TruncateForCheckpoint(
                    BatchTestDisplayName.Resolve(rowHost.CustomName, i));
                var paramLine = BatchTestParameterFormatter.BuildCompact(mx, threads, solidArchive);
                var perfStartSerial = _resourceMonitor.TotalSampleSerial + 1;
                _resourceMonitor.NotifyBatchStepStarting(i, checkpointLabel, paramLine);
                StatusText.Text = $"Batch step {i + 1} of {specs.Count}…";
                var mmtLog = threads <= 0 ? "Auto" : threads.ToString();
                var solidLog = solidArchive ? "on" : "off";
                _log.Log("benchmark", $"Batch {i + 1}/{specs.Count}: -mx={mx} -mmt={mmtLog} -ms={solidLog} (native .7z)");
                var opts = CompressionOptionsResolver.FromExplicit(mx, threads, solidArchive);
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
                                        reportGameTrack: null,
                                        cancellationToken: token)
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
                var perfSummary = _resourceMonitor.TryComputeSummary(perfStartSerial, serial);

                var entry = SandboxBenchmarkFormat.FromResult(backup, result, perfSummary);
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

    private static bool IsSolidArchive(BatchRowHost host)
    {
        if (host.ModeCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return string.Equals(tag, "solid", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private sealed class BatchRowHost
    {
        public TextBlock Label { get; set; } = null!;
        public Button RenameButton { get; set; } = null!;
        public string? CustomName { get; set; }
        public Button RemoveButton { get; set; } = null!;
        public Slider LevelSlider { get; set; } = null!;
        public ComboBox ModeCombo { get; set; } = null!;
        public Slider ThreadSlider { get; set; } = null!;
        public TextBlock ThreadLabel { get; set; } = null!;
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
