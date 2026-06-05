using System.ComponentModel;
using System.Linq;
using System.Text;
using GSBT.WinUI;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxBenchmarkView : UserControl
{
    /// <summary>Icon-only actions on result cards (compact; smaller than 28 px).</summary>
    private const double CardIconButtonSize = 22;

    private readonly MainViewModel _vm;
    private readonly SandboxCompressionBenchmarkStore _store;
    private readonly SandboxLogHub _log;
    private readonly SandboxMonitorSession _monitorSession;
    private readonly Window _ownerWindow;
    private bool _benchmarkRunActive;
    private bool _historyLoaded;
    private bool _historyReloadInFlight;

    public SandboxBenchmarkView(
        MainViewModel vm,
        SandboxCompressionBenchmarkStore store,
        SandboxLogHub log,
        SandboxMonitorSession monitorSession,
        Window ownerWindow)
    {
        _vm = vm;
        _store = store;
        _log = log;
        _monitorSession = monitorSession;
        _ownerWindow = ownerWindow;
        InitializeComponent();
        Loaded += SandboxBenchmarkView_Loaded;
        Unloaded += SandboxBenchmarkView_Unloaded;
        KeyDown += SandboxBenchmarkView_KeyDown;
    }

    private void SandboxBenchmarkView_Loaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged += VmOnPropertyChanged;
        ThemeBridge.ShellThemeChanged += OnGlobalShellThemeChanged;
        _ = EnsureHistoryLoadedAsync();
        RefreshRunAvailability();
    }

    /// <summary>Called from <see cref="SandboxMonitorWindow.ApplyShellChromeTheme"/> for immediate theme sync.</summary>
    public void OnShellThemeChanged(ElementTheme theme)
    {
        RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void OnGlobalShellThemeChanged(ElementTheme theme) =>
        DispatcherQueue.TryEnqueue(() => OnShellThemeChanged(theme));

    private void SandboxBenchmarkView_Unloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged -= VmOnPropertyChanged;
        ThemeBridge.ShellThemeChanged -= OnGlobalShellThemeChanged;
        KeyDown -= SandboxBenchmarkView_KeyDown;
    }

    private void SandboxBenchmarkView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || !_benchmarkRunActive || !_vm.CanCancelOperation)
        {
            return;
        }

        _vm.CancelOperation();
        RunStatusText.Text = "Cancelling…";
        _log.Log("benchmark", "Cancel requested (Escape, single run).");
        e.Handled = true;
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsBusy) or nameof(MainViewModel.IsScanning) or nameof(MainViewModel.CanCancelOperation))
        {
            DispatcherQueue.TryEnqueue(RefreshRunAvailability);
        }
    }

    private void RefreshRunAvailability()
    {
        RunBenchmarkButton.IsEnabled = CanRunBenchmark() && !_benchmarkRunActive;
        CancelBenchmarkButton.IsEnabled = _benchmarkRunActive && _vm.CanCancelOperation;
        ClearAllHistoryButton.IsEnabled = !_benchmarkRunActive;
    }

    private bool CanRunBenchmark()
    {
        var p = _vm.GetEffectiveBackupRootForCompressPrompt();
        return !string.IsNullOrWhiteSpace(p)
            && Directory.Exists(p)
            && !_vm.IsBusy
            && !_vm.IsScanning;
    }

    /// <summary>Loads saved-run cards once (avoids empty-panel flash when re-opening the pane).</summary>
    public Task EnsureHistoryLoadedAsync() => ReloadListAsync(force: false);

    /// <summary>Refreshes saved-run cards from disk (e.g. after batch steps on another monitor tab).</summary>
    public Task ReloadHistoryAsync() => ReloadListAsync(force: true);

    private async Task ReloadListAsync(bool force)
    {
        if (_historyReloadInFlight)
        {
            return;
        }

        if (!force && _historyLoaded)
        {
            return;
        }

        _historyReloadInFlight = true;
        try
        {
            var rows = await _store.LoadAsync().ConfigureAwait(true);
            var cards = rows.Select(BuildRunCard).ToList();
            RunsPanel.Children.Clear();
            foreach (var card in cards)
            {
                RunsPanel.Children.Add(card);
            }

            RunStatusText.Text = rows.Count == 0
                ? "No saved runs yet. Use Run benchmark (creates a real archive in your backup folder, same as main Compress)."
                : $"{rows.Count} saved run(s). File: {AppPaths.SandboxCompressionBenchmarksPath}";
            _historyLoaded = true;
        }
        finally
        {
            _historyReloadInFlight = false;
        }
    }

    private bool IsSandboxPanelDarkChrome() => ActualTheme == ElementTheme.Dark;

    private Border BuildRunCard(SandboxCompressionBenchmarkEntry entry)
    {
        var dark = IsSandboxPanelDarkChrome();
        var titleBrush = ThemeBridge.GetGsbtBrush(dark, entry.Success ? "GsbtBenchmarkSuccessTitleBrush" : "GsbtBenchmarkFailureTitleBrush");
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBridge.GetGsbtBrush(dark, "GsbtBorderBrush"),
            Background = ThemeBridge.GetGsbtBrush(dark, "GsbtCardBgBrush"),
            Padding = new Thickness(12, 10, 12, 10),
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = entry.TitleLine,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = titleBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        ToggleButton? detailsToggle = null;
        if (!string.IsNullOrWhiteSpace(entry.ExtraDetailText))
        {
            detailsToggle = new ToggleButton
            {
                MinWidth = CardIconButtonSize,
                MinHeight = CardIconButtonSize,
                MaxWidth = CardIconButtonSize,
                MaxHeight = CardIconButtonSize,
                Padding = new Thickness(2),
                Content = new FontIcon { Glyph = "\uE70D", FontSize = 10 },
            };
            AutomationProperties.SetName(detailsToggle, "Toggle extra benchmark details");
            ToolTipService.SetToolTip(detailsToggle, "Show or hide extra details (keyboard: Space when focused)");
            actions.Children.Add(detailsToggle);
        }

        var more = new Button
        {
            MinWidth = CardIconButtonSize,
            MinHeight = CardIconButtonSize,
            MaxWidth = CardIconButtonSize,
            MaxHeight = CardIconButtonSize,
            Padding = new Thickness(2),
            Content = new TextBlock
            {
                Text = "⋯",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        AutomationProperties.SetName(more, "Row options");
        ToolTipService.SetToolTip(more, "Copy or export this run");
        var fly = new MenuFlyout();
        var copyItem = new MenuFlyoutItem { Text = "Copy this run (plain text)" };
        copyItem.Click += (_, _) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(FormatEntryPlain(entry));
                Clipboard.SetContent(package);
                RunStatusText.Text = "Copied one run to the clipboard.";
            }
            catch (Exception ex)
            {
                RunStatusText.Text = $"Copy failed: {ex.Message}";
            }
        };
        var exportItem = new MenuFlyoutItem { Text = "Export this run as JSON…" };
        exportItem.Click += async (_, _) => await ExportSingleEntryAsync(entry).ConfigureAwait(true);
        fly.Items.Add(copyItem);
        fly.Items.Add(exportItem);
        more.Flyout = fly;

        var del = new Button
        {
            MinWidth = CardIconButtonSize,
            MinHeight = CardIconButtonSize,
            MaxWidth = CardIconButtonSize,
            MaxHeight = CardIconButtonSize,
            Padding = new Thickness(2),
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
        };
        AutomationProperties.SetName(del, "Remove benchmark row");
        ToolTipService.SetToolTip(del, "Remove this row from history");
        del.Click += async (_, _) =>
        {
            if (XamlRoot is null)
            {
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "Remove benchmark row?",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Text = "This deletes only this card from saved history (not the archive file on disk).",
                },
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            if (await GsbtContentDialog.ShowAsync(confirm) != ContentDialogResult.Primary)
            {
                return;
            }

            await _store.RemoveByIdAsync(entry.Id).ConfigureAwait(true);
            _log.Log("benchmark", $"Removed row {entry.Id}");
            await ReloadListAsync(force: true).ConfigureAwait(true);
        };

        actions.Children.Add(more);
        actions.Children.Add(del);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var mono = new FontFamily("Consolas, Cascadia Mono, Courier New");
        var bodyColor = ThemeBridge.GetGsbtBrush(dark, "GsbtMonoBodyBrush");
        var dimColor = ThemeBridge.GetGsbtBrush(dark, "GsbtMonoMutedBrush");

        if (!string.IsNullOrWhiteSpace(entry.PriorityDetailText))
        {
            var priority = new TextBlock
            {
                Text = entry.PriorityDetailText,
                FontFamily = mono,
                FontSize = 11,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = bodyColor,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(priority, 1);
            root.Children.Add(priority);

            if (!string.IsNullOrWhiteSpace(entry.ExtraDetailText))
            {
                var extra = new TextBlock
                {
                    Text = entry.ExtraDetailText,
                    FontFamily = mono,
                    FontSize = 11,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Foreground = dimColor,
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                if (detailsToggle is not null)
                {
                    static void ApplyChevron(ToggleButton tb, bool expanded) =>
                        ((FontIcon)tb.Content).Glyph = expanded ? "\uE70E" : "\uE70D";

                    detailsToggle.Checked += (_, _) =>
                    {
                        extra.Visibility = Visibility.Visible;
                        ApplyChevron(detailsToggle, true);
                    };
                    detailsToggle.Unchecked += (_, _) =>
                    {
                        extra.Visibility = Visibility.Collapsed;
                        ApplyChevron(detailsToggle, false);
                    };
                }

                Grid.SetRow(extra, 2);
                root.Children.Add(extra);
            }
        }
        else
        {
            var legacy = new TextBlock
            {
                Text = entry.DetailText,
                FontFamily = mono,
                FontSize = 11,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = bodyColor,
                Margin = new Thickness(0, 10, 0, 0),
            };
            Grid.SetRow(legacy, 1);
            Grid.SetRowSpan(legacy, 2);
            root.Children.Add(legacy);
        }

        border.Child = root;
        return border;
    }

    private static string FormatEntryPlain(SandboxCompressionBenchmarkEntry e)
    {
        if (!string.IsNullOrWhiteSpace(e.PriorityDetailText))
        {
            return e.TitleLine
                + Environment.NewLine
                + e.PriorityDetailText
                + Environment.NewLine
                + Environment.NewLine
                + (e.ExtraDetailText ?? string.Empty);
        }

        return e.TitleLine + Environment.NewLine + e.DetailText;
    }

    private async Task ExportSingleEntryAsync(SandboxCompressionBenchmarkEntry entry)
    {
        try
        {
            var json = _store.ExportEntriesJson(new[] { entry });
            var save = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"gsbt-benchmark-{entry.Id:N}.json",
            };
            save.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitializeWithWindow.Initialize(save, WindowNative.GetWindowHandle(_ownerWindow));
            var file = await save.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, json);
            RunStatusText.Text = $"Exported one run to {file.Path}";
            _log.Log("benchmark", $"Exported single entry → {file.Path}");
        }
        catch (Exception ex)
        {
            RunStatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private async void RunBenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRunBenchmark())
        {
            RunStatusText.Text = "Cannot run: wait until idle, and ensure a valid backup folder is set.";
            return;
        }

        _benchmarkRunActive = true;
        RefreshRunAvailability();
        RunStatusText.Text = "Running…";
        var backup = _vm.GetEffectiveBackupRootForCompressPrompt() ?? "—";
        _log.Log("benchmark", $"Start run — backup: {backup}");
        try
        {
            _monitorSession.SetCompressionWorkloadActive(true);
            var (msg, res) = await _vm.CompressBackupFolderWithResultAsync().ConfigureAwait(true);
            if (res is not null)
            {
                var row = SandboxBenchmarkFormat.FromResult(backup, res);
                await _store.AppendAsync(row).ConfigureAwait(true);
                _log.Log("benchmark", $"Recorded row {row.TitleLine}");
            }
            else
            {
                _log.Log("benchmark", $"No structured result — {msg}");
            }

            RunStatusText.Text = msg;
            await ReloadListAsync(force: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RunStatusText.Text = $"Error: {ex.Message}";
            _log.Log("benchmark", "ERROR: " + ex.Message);
        }
        finally
        {
            _monitorSession.SetCompressionWorkloadActive(false);
            _benchmarkRunActive = false;
            RefreshRunAvailability();
        }
    }

    private void CancelBenchmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_benchmarkRunActive && _vm.CanCancelOperation)
        {
            _vm.CancelOperation();
            RunStatusText.Text = "Cancelling…";
            _log.Log("benchmark", "Cancel requested (single run).");
        }
    }

    private async void ClearAllHistory_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Clear benchmark history?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Text = "This removes all saved compression benchmark rows from this machine (the JSON file is rewritten empty). Export first if you need a backup.",
            },
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await GsbtContentDialog.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        await _store.SaveAllAsync(Array.Empty<SandboxCompressionBenchmarkEntry>()).ConfigureAwait(true);
        _log.Log("benchmark", "History cleared (all).");
        await ReloadListAsync(force: true).ConfigureAwait(true);
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = await _store.ExportJsonAsync().ConfigureAwait(true);
            var save = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "gsbt-compression-benchmarks.json",
            };
            save.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            InitializeWithWindow.Initialize(save, WindowNative.GetWindowHandle(_ownerWindow));
            var file = await save.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            await FileIO.WriteTextAsync(file, json);
            RunStatusText.Text = $"Exported to {file.Path}";
            _log.Log("benchmark", $"Exported JSON → {file.Path}");
        }
        catch (Exception ex)
        {
            RunStatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private async void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var open = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            open.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(open, WindowNative.GetWindowHandle(_ownerWindow));
            var file = await open.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var text = await FileIO.ReadTextAsync(file);
            var added = await _store.MergeImportFromJsonAsync(text).ConfigureAwait(true);
            RunStatusText.Text = added == 0
                ? "Import finished — no new rows (duplicate IDs or invalid file)."
                : $"Imported {added} new row(s) from {file.Name}.";
            _log.Log("benchmark", $"Import merge from {file.Path} → +{added} rows");
            await ReloadListAsync(force: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RunStatusText.Text = $"Import failed: {ex.Message}";
        }
    }

    private async void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = await _store.LoadAsync().ConfigureAwait(true);
            var sb = new StringBuilder();
            var i = 1;
            foreach (var row in rows)
            {
                sb.AppendLine($"=== Run {i++} ===");
                sb.AppendLine(FormatEntryPlain(row));
                sb.AppendLine();
            }

            var package = new DataPackage();
            package.SetText(sb.ToString());
            Clipboard.SetContent(package);
            RunStatusText.Text = "Copied all runs to the clipboard (plain text).";
        }
        catch (Exception ex)
        {
            RunStatusText.Text = $"Copy failed: {ex.Message}";
        }
    }
}
