using System.Diagnostics;
using System.Linq;
using GSBT.Core.Common;
using GSBT.Core.Services;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Microsoft.UI.Text;
using WinRT.Interop;
namespace GSBT.WinUI.Views;

public sealed partial class MainPage
{
    private void WireBackupTeachingTip()
    {
        ViewModel.TeachingTipBackupBulkRequested += ViewModel_TeachingTipBackupBulkRequested;
    }

    private void UnwireBackupTeachingTip()
    {
        ViewModel.TeachingTipBackupBulkRequested -= ViewModel_TeachingTipBackupBulkRequested;
    }

    private void ViewModel_TeachingTipBackupBulkRequested(object? sender, EventArgs e)
    {
        try
        {
            BackupBulkTeachingTip.Target = BackupButton;
            BackupBulkTeachingTip.IsOpen = true;
        }
        catch
        {
            // ignore tip failures
        }
    }

    private void BackupBulkTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        ViewModel.MarkBackupTeachingTipDismissed();
    }

    /// <summary>Fluent <see cref="ContentDialog"/> — this is the WinUI 11-style modal (not the older Win32 message box look).</summary>
    private async Task<(bool Ok, string Path, bool UseAsDefault)> ShowSelectBackupDestinationDialogAsync()
    {
        var intro = new TextBlock
        {
            Text =
                "No backup folder is set yet. Choose where game save backups should be stored.\n\n"
                + "You can change this anytime in Settings.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 12),
            MaxWidth = 420
        };

        var suggestedBackupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GSBT_Backups");
        var pathBox = new TextBox
        {
            Text = suggestedBackupDir,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var browse = new Button { Content = "Browse…", MinWidth = 88 };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                var hwnd = WindowNative.GetWindowHandle(App.MainWindowRef);
                InitializeWithWindow.Initialize(picker, hwnd);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    pathBox.Text = folder.Path;
                }
            }
            catch
            {
                // ignore
            }
        };

        var pathRow = new Grid { ColumnSpacing = 8, MinWidth = 380 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathBox, 0);
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(pathBox);
        pathRow.Children.Add(browse);

        var remember = new CheckBox
        {
            Content = "Don't show this message again (use this folder as default)",
            Margin = new Thickness(0, 12, 0, 0),
            IsChecked = true
        };

        var root = new StackPanel { Spacing = 0 };
        root.Children.Add(intro);
        root.Children.Add(pathRow);
        root.Children.Add(remember);

        var dialog = new ContentDialog
        {
            Title = "Select Backup Destination",
            Content = root,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        var result = await GsbtContentDialog.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return (false, string.Empty, false);
        }

        var path = pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _ = ShowStatusToastAsync("Choose a folder or enter a valid path.");
            return (false, string.Empty, false);
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            _ = ShowStatusToastAsync("That path is not valid.");
            return (false, string.Empty, false);
        }

        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                _ = ShowStatusToastAsync($"Could not create folder: {ex.Message}");
                return (false, string.Empty, false);
            }
        }

        var useDefault = remember.IsChecked == true;
        return (true, path, useDefault);
    }

    private async Task<(bool Ok, string NewName)> ShowRenameGameDialogAsync(string currentName)
    {
        var hint = new TextBlock
        {
            Text = "This updates the name in your saved catalog (the folder path stays the same).",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 12
        };

        var nameBox = new TextBox
        {
            Header = "Game name",
            Text = currentName,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var nameWarn = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xff, 0x99, 0x99)),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420
        };

        var root = new StackPanel { Spacing = 0 };
        root.Children.Add(hint);
        root.Children.Add(nameBox);
        root.Children.Add(nameWarn);

        nameBox.TextChanged += (_, _) =>
        {
            nameWarn.Visibility = Visibility.Collapsed;
        };

        var dialog = new ContentDialog
        {
            Title = "Rename game",
            Content = root,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var t = (nameBox.Text ?? string.Empty).Trim();
            if (!GameNameInputValidation.IsValidGameNameForStorage(t, out var err))
            {
                nameWarn.Text = err ?? "Invalid name.";
                nameWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            var cleaned = GameDisplayName.CleanDisplayName(t);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                nameWarn.Text = "Enter a printable name.";
                nameWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            if (!GameNameInputValidation.IsValidGameNameForStorage(cleaned, out var err2))
            {
                nameWarn.Text = err2 ?? "Invalid name.";
                nameWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        var result = await GsbtContentDialog.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return (false, string.Empty);
        }

        var finalName = GameDisplayName.CleanDisplayName((nameBox.Text ?? string.Empty).Trim());
        return (true, finalName);
    }

    private async Task<bool> ShowManifestRefreshConfirmDialogAsync()
    {
        var body = new TextBlock
        {
            Text =
                "Downloads the latest save-path manifest from GitHub (needs internet), then scans your games. "
                + "If the download fails, the app keeps using the manifest already on disk.",
            TextWrapping = TextWrapping.WrapWholeWords,
            MaxWidth = 420,
            FontSize = 13
        };

        var dialog = new ContentDialog
        {
            Title = "Download latest manifest?",
            Content = body,
            PrimaryButtonText = "OK",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        var result = await GsbtContentDialog.ShowAsync(dialog);
        return result == ContentDialogResult.Primary;
    }

    private async Task<(bool Ok, string RawInput)> ShowAddRegistrySaveDialogAsync()
    {
        var hint = new TextBlock
        {
            Text = "Paste a registry key path (for example from Regedit’s address bar). If a value in that key points to a folder on disk, it becomes the save location. Otherwise the key itself can be stored as an in-registry save location.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 12
        };

        var box = new TextBox
        {
            Header = "Registry path",
            PlaceholderText = @"HKCU\Software\MyGame\Save",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(hint);
        root.Children.Add(box);

        var dialog = new ContentDialog
        {
            Title = "Add registry save location",
            Content = root,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        var result = await GsbtContentDialog.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return (false, string.Empty);
        }

        return (true, box.Text ?? string.Empty);
    }

    private async Task<bool> ShowBackupFolderCollisionDialogAsync(IReadOnlyList<string> messages)
    {
        var intro = new TextBlock
        {
            Text = "These games would use the same backup folder name after removing characters Windows does not allow in paths. Retention may delete the wrong backups.",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420
        };

        var list = new StackPanel { Spacing = 6 };
        foreach (var msg in messages)
        {
            list.Children.Add(new TextBlock
            {
                Text = msg,
                TextWrapping = TextWrapping.WrapWholeWords,
                MaxWidth = 420
            });
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = list
        };

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(intro);
        root.Children.Add(scroll);

        var dialog = new ContentDialog
        {
            Title = "Backup folder name conflict",
            Content = root,
            PrimaryButtonText = "Continue anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        ApplyShellThemeToContentDialog(dialog);
        var result = await GsbtContentDialog.ShowAsync(dialog);
        return result == ContentDialogResult.Primary;
    }

    private enum CustomGameDialogSaveMode
    {
        DiskFolder,
        Registry
    }

    private async Task<(bool Ok, string GameName, CustomGameDialogSaveMode Mode, string FolderPath, string RegistryRaw)> ShowAddCustomGameDialogAsync()
    {
        var nameBox = new TextBox
        {
            Header = "Game name",
            PlaceholderText = "My Game",
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var nameWarn = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xff, 0x99, 0x99)),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420
        };

        var diskRadio = new RadioButton
        {
            Content = "Select folder",
            IsChecked = true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var registryRadio = new RadioButton
        {
            Content = "Select registry",
            Margin = new Thickness(0, 12, 0, 0)
        };

        var folderBox = new TextBox
        {
            PlaceholderText = "Save folder on disk…",
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var folderWarn = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xff, 0x99, 0x99)),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420
        };

        var browse = new Button { Content = "Browse…", MinWidth = 88 };

        var pathRow = new Grid { ColumnSpacing = 8, MinWidth = 380 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(folderBox, 0);
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(folderBox);
        pathRow.Children.Add(browse);

        var registryBox = new TextBox
        {
            PlaceholderText = @"HKCU\Software\… (paste from Regedit)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 88,
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var registryWarn = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xff, 0x99, 0x99)),
            Margin = new Thickness(0, 4, 0, 0),
            MaxWidth = 420
        };

        void ApplySaveModeUi()
        {
            var disk = diskRadio.IsChecked == true;
            folderBox.IsEnabled = disk;
            browse.IsEnabled = disk;
            registryBox.IsEnabled = !disk;
        }

        diskRadio.Checked += (_, _) => ApplySaveModeUi();
        registryRadio.Checked += (_, _) => ApplySaveModeUi();
        folderBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(folderBox.Text))
            {
                diskRadio.IsChecked = true;
            }

            ApplySaveModeUi();
        };
        registryBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(registryBox.Text))
            {
                registryRadio.IsChecked = true;
            }

            ApplySaveModeUi();
        };

        browse.Click += async (_, _) =>
        {
            diskRadio.IsChecked = true;
            ApplySaveModeUi();
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                var hwnd = WindowNative.GetWindowHandle(App.MainWindowRef);
                InitializeWithWindow.Initialize(picker, hwnd);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    folderBox.Text = folder.Path;
                }
            }
            catch
            {
                // ignore
            }
        };

        void SyncNameHint()
        {
            var t = nameBox.Text ?? string.Empty;
            if (GameNameInputValidation.ContainsInvalidFileNameCharacters(t))
            {
                nameWarn.Text =
                    $"Remove characters that cannot be used in file names ({GameNameInputValidation.InvalidFileNameCharactersForUserMessage}).";
                nameWarn.Visibility = Visibility.Visible;
            }
            else
            {
                nameWarn.Visibility = Visibility.Collapsed;
            }
        }

        nameBox.TextChanged += (_, _) => SyncNameHint();

        var root = new StackPanel { Spacing = 0 };
        root.Children.Add(nameBox);
        root.Children.Add(nameWarn);
        root.Children.Add(diskRadio);
        root.Children.Add(pathRow);
        root.Children.Add(folderWarn);
        root.Children.Add(registryRadio);
        root.Children.Add(registryBox);
        root.Children.Add(registryWarn);
        ApplySaveModeUi();

        var dialog = new ContentDialog
        {
            Title = "Add Custom Game",
            Content = root,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        dialog.PrimaryButtonClick += (_, args) =>
        {
            folderWarn.Visibility = Visibility.Collapsed;
            registryWarn.Visibility = Visibility.Collapsed;
            var gameName = (nameBox.Text ?? string.Empty).Trim();
            if (!GameNameInputValidation.IsValidGameNameForStorage(gameName, out var nameErr))
            {
                nameWarn.Text = nameErr ?? "Invalid name.";
                nameWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            if (registryRadio.IsChecked == true)
            {
                var reg = (registryBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(reg))
                {
                    registryWarn.Text = "Enter a registry path or paste from Regedit.";
                    registryWarn.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }

                if (RegistrySaveResolver.LooksLikeFilesystemPath(reg))
                {
                    registryWarn.Text = "That looks like a folder path. Use the disk folder option instead.";
                    registryWarn.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }

                return;
            }

            var folder = (folderBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folderWarn.Text = "Choose or enter a save folder.";
                folderWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            try
            {
                var full = Path.GetFullPath(folder);
                if (!Directory.Exists(full))
                {
                    folderWarn.Text = "That folder does not exist.";
                    folderWarn.Visibility = Visibility.Visible;
                    args.Cancel = true;
                }
            }
            catch
            {
                folderWarn.Text = "That folder path is not valid.";
                folderWarn.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        var result = await GsbtContentDialog.ShowAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return (false, string.Empty, CustomGameDialogSaveMode.DiskFolder, string.Empty, string.Empty);
        }

        var gameNameFinal = (nameBox.Text ?? string.Empty).Trim();
        if (registryRadio.IsChecked == true)
        {
            return (true, gameNameFinal, CustomGameDialogSaveMode.Registry, string.Empty, (registryBox.Text ?? string.Empty).Trim());
        }

        return (true, gameNameFinal, CustomGameDialogSaveMode.DiskFolder, (folderBox.Text ?? string.Empty).Trim(), string.Empty);
    }

    /// <param name="warningOnly">When true (estimate setting off), only unusually large folders are listed — no clutter from normal-sized games.</param>
    private async Task<bool> ShowBackupEstimateConfirmDialogAsync(BackupSizeEstimateSummary summary, bool warningOnly)
    {
        var content = BuildBackupEstimateDialogContent(summary, warningOnly, out var estimateScroll);
        var dialog = new ContentDialog
        {
            Title = "Backup estimate",
            Content = content,
            PrimaryButtonText = "Start backup",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(dialog);

        void FitEstimateScrollViewport()
        {
            try
            {
                var hostH = XamlRoot?.Size.Height ?? 640;
                // Leave room for dialog chrome, title, intro lines, and primary/close buttons on short windows.
                estimateScroll.MaxHeight = Math.Max(96, Math.Min(540, hostH - 248));
            }
            catch
            {
                estimateScroll.MaxHeight = 260;
            }
        }

        dialog.Opened += (_, _) => FitEstimateScrollViewport();
        dialog.SizeChanged += (_, _) => FitEstimateScrollViewport();

        var dr = await GsbtContentDialog.ShowAsync(dialog);
        return dr == ContentDialogResult.Primary;
    }

    private FrameworkElement BuildBackupEstimateDialogContent(BackupSizeEstimateSummary summary, bool warningOnly, out ScrollViewer estimateScroll)
    {
        var shellDark = ThemeBridge.IsShellDarkTheme();
        Brush Muted() => ResolveShellDialogBrush(
            "TextFillColorSecondaryBrush",
            ThemeBridge.GetGsbtBrush(shellDark, "GsbtSecondaryLabelBrush"));
        var accentStat = ThemeBridge.GetGsbtBrush(shellDark, "GsbtEstimateStatBrush");
        var bodyFg = ThemeBridge.GetGsbtBrush(shellDark, "GsbtBodyTextBrush");
        var goodSize = ThemeBridge.GetGsbtBrush(shellDark, "GsbtEstimateGoodBrush");
        var warnSize = ThemeBridge.GetGsbtBrush(shellDark, "GsbtEstimateWarnBrush");
        var badSize = ThemeBridge.GetGsbtBrush(shellDark, "GsbtEstimateBadBrush");
        var cardBg = ResolveShellDialogBrush("GsbtCardBgBrush", new SolidColorBrush(Color.FromArgb(255, 0x2d, 0x2d, 0x2d)));

        Brush PickSizeBrush(BackupSizeSeverity s) =>
            s switch
            {
                BackupSizeSeverity.Suspicious => badSize,
                BackupSizeSeverity.Large => warnSize,
                _ => goodSize
            };

        var displayEntries = warningOnly
            ? summary.Entries
                .Where(e =>
                    !e.IsRegistryOnly
                    && (e.Severity == BackupSizeSeverity.Large || e.Severity == BackupSizeSeverity.Suspicious))
                .ToList()
            : summary.Entries.ToList();

        var diskTail = displayEntries.Where(e => !e.IsRegistryOnly).ToList();
        var tailBytes = diskTail.Sum(e => e.Bytes);
        var tailFiles = diskTail.Sum(e => e.FileCount);
        var tailGames = displayEntries.Count;
        var tailDiskFolders = diskTail.Count;
        var tailReg = displayEntries.Count(e => e.IsRegistryOnly);

        var column = new StackPanel { Spacing = 10, MinWidth = 438, MaxWidth = 438 };

        var subtitle = new TextBlock
        {
            Text = warningOnly
                ? "These save folders exceed the large-size threshold (estimate is off in Settings — showing warnings only)."
                : "Here is what will be copied. Choose whether to start the backup.",
            FontSize = 12,
            Foreground = Muted(),
            TextWrapping = TextWrapping.WrapWholeWords
        };
        column.Children.Add(subtitle);

        var destPath = string.IsNullOrWhiteSpace(summary.BackupDestinationDisplay)
            ? "(not set)"
            : summary.BackupDestinationDisplay;
        column.Children.Add(
            new TextBlock
            {
                Text = $"Backup to: {destPath}",
                FontSize = 11,
                Foreground = Muted(),
                TextWrapping = TextWrapping.WrapWholeWords
            });

        var innerStack = new StackPanel { Spacing = 10, Padding = new Thickness(0, 0, 16, 16) };

        var perHeader = new TextBlock
        {
            Text = "Per game",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = bodyFg,
        };
        innerStack.Children.Add(perHeader);

        var summaryPanel = new StackPanel { Spacing = 6 };
        var vGames = AddStatRow(summaryPanel, "Games in this backup:", tailGames.ToString(), accentStat, Muted());
        var vDisk = AddStatRow(summaryPanel, "Save folders on disk:", tailDiskFolders.ToString(), accentStat, Muted());
        var vFiles = AddStatRow(summaryPanel, "Approx. files to copy:", tailFiles.ToString(), accentStat, Muted());
        var vSize = AddStatRow(
            summaryPanel,
            "Approx. total size:",
            BackupFolderSizeEstimator.FormatApproximateSizeIec(tailBytes),
            goodSize,
            Muted());
        var vReg = AddStatRow(summaryPanel, "Registry-only saves:", tailReg.ToString(), accentStat, Muted());

        foreach (var entry in displayEntries)
        {
            var captured = entry;
            innerStack.Children.Add(
                BuildPerGameEstimateBlock(
                    captured,
                    innerStack,
                    Muted,
                    accentStat,
                    PickSizeBrush,
                    goodSize,
                    warnSize,
                    badSize,
                    () =>
                    {
                        if (captured.IsRegistryOnly)
                        {
                            tailReg = Math.Max(0, tailReg - 1);
                            tailGames = Math.Max(0, tailGames - 1);
                            vReg.Text = tailReg.ToString();
                            vGames.Text = tailGames.ToString();
                            return;
                        }

                        tailGames = Math.Max(0, tailGames - 1);
                        tailDiskFolders = Math.Max(0, tailDiskFolders - 1);
                        tailFiles = Math.Max(0, tailFiles - captured.FileCount);
                        tailBytes = Math.Max(0L, tailBytes - captured.Bytes);
                        vGames.Text = tailGames.ToString();
                        vDisk.Text = tailDiskFolders.ToString();
                        vFiles.Text = tailFiles.ToString();
                        vSize.Text = BackupFolderSizeEstimator.FormatApproximateSizeIec(tailBytes);
                    }));
        }

        innerStack.Children.Add(
            new TextBlock
            {
                Text = "Summary",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = bodyFg,
            });
        innerStack.Children.Add(summaryPanel);

        estimateScroll = new ScrollViewer
        {
            MaxHeight = 280,
            Padding = new Thickness(0, 0, 4, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = innerStack
        };

        column.Children.Add(
            new Border
            {
                Background = cardBg,
                CornerRadius = new CornerRadius(8),
                BorderBrush = ResolveShellDialogBrush("GsbtBorderBrush", new SolidColorBrush(Color.FromArgb(255, 0x3e, 0x3e, 0x42))),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Child = estimateScroll
            });

        return column;
    }

    private UIElement BuildPerGameEstimateBlock(
        BackupSizeEstimateEntry entry,
        Panel hostPanel,
        Func<Brush> muted,
        Brush accentStat,
        Func<BackupSizeSeverity, Brush> pickSizeBrush,
        Brush goodSize,
        Brush warnSize,
        Brush badSize,
        Action adjustSummaryAfterRemove)
    {
        if (entry.IsRegistryOnly)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4, 0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = entry.GameName,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtBodyTextBrush"),
            });
            row.Children.Add(
                new TextBlock
                {
                    Text = "— registry export (tiny)",
                    FontSize = 11,
                    Foreground = muted()
                });
            return row;
        }

        var block = new StackPanel { Spacing = 4, Margin = new Thickness(8, 6, 0, 8), Tag = entry.GameName };

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleTb = new TextBlock
        {
            Text = entry.GameName,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), "GsbtBodyTextBrush"),
        };
        Grid.SetColumn(titleTb, 0);
        titleRow.Children.Add(titleTb);

        var folderPath = entry.SaveFolderPath;
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var openBtn = new Button
            {
                Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
                Padding = new Thickness(4, 2, 4, 2),
                MinHeight = 26,
                MinWidth = 30,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(openBtn, "Open save folder in File Explorer (keyboard: activate focused button with Space or Enter)");
            openBtn.Content = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                Glyph = "\uE838",
                FontSize = 14
            };
            openBtn.Click += (_, _) => TryOpenFolderInExplorer(folderPath);
            Grid.SetColumn(openBtn, 1);
            titleRow.Children.Add(openBtn);
        }

        block.Children.Add(titleRow);

        var filesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0) };
        filesRow.Children.Add(new TextBlock { Text = "Files:", FontSize = 11, Foreground = muted() });
        filesRow.Children.Add(
            new TextBlock
            {
                Text = entry.FileCount.ToString(),
                FontSize = 11,
                Foreground = accentStat,
                FontWeight = FontWeights.SemiBold
            });
        block.Children.Add(filesRow);

        var sizeLabel = new TextBlock { Text = "Size:", FontSize = 11, Foreground = muted() };
        var sizeValue = new TextBlock
        {
            Text = BackupFolderSizeEstimator.FormatApproximateSizeIec(entry.Bytes),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = pickSizeBrush(entry.Severity)
        };
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0) };
        sizeRow.Children.Add(sizeLabel);
        sizeRow.Children.Add(sizeValue);
        block.Children.Add(sizeRow);

        TextBlock? hint = null;
        if (entry.Severity == BackupSizeSeverity.Suspicious)
        {
            hint = new TextBlock
            {
                Text =
                    "Very large for typical saves — confirm this path. Wrong-era manifest matches can point at installs or unrelated data.",
                FontSize = 11,
                Foreground = badSize,
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(10, 2, 0, 0)
            };
            block.Children.Add(hint);
        }
        else if (entry.Severity == BackupSizeSeverity.Large)
        {
            hint = new TextBlock
            {
                Text = "Unusually large — double-check if this isn’t an install or wrong game folder.",
                FontSize = 11,
                Foreground = warnSize,
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(10, 2, 0, 0)
            };
            block.Children.Add(hint);
        }

        if (entry.Severity is BackupSizeSeverity.Large or BackupSizeSeverity.Suspicious)
        {
            var defaultBtnStyle = Application.Current.Resources["DefaultButtonStyle"] as Style;
            var btnGrid = new Grid
            {
                ColumnSpacing = 8,
                Margin = new Thickness(8, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var yesBtn = new Button
            {
                Content = "✓ Yes, this path is correct.",
                Style = defaultBtnStyle,
                FontSize = 11,
                MinHeight = 26,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            var noBtn = new Button
            {
                Content = "✗ No, this path isn’t correct",
                Style = defaultBtnStyle,
                FontSize = 11,
                MinHeight = 26,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(yesBtn, 0);
            Grid.SetColumn(noBtn, 1);
            btnGrid.Children.Add(yesBtn);
            btnGrid.Children.Add(noBtn);

            yesBtn.Click += (_, _) =>
            {
                ViewModel.TrustLargeSavePath(entry.GameName);
                sizeValue.Foreground = goodSize;
                if (hint is not null)
                {
                    hint.Visibility = Visibility.Collapsed;
                }

                block.Children.Remove(btnGrid);
            };

            // Nested ContentDialog crashes WinUI; use a flyout for confirmation while the estimate dialog stays open.
            var confirmFlyout = new Flyout();
            var confirmPanel = new StackPanel { Spacing = 10, Padding = new Thickness(12), MaxWidth = 340 };
            confirmPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Remove “{entry.GameName}” from your game list? You can undo with Ctrl+Z right after if it was a mistake.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    FontSize = 12
                });
            var confirmRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var removeConfirmBtn = new Button
            {
                Content = "Remove",
                Style = defaultBtnStyle,
                FontSize = 11,
                MinHeight = 26,
                Padding = new Thickness(10, 2, 10, 2)
            };
            var cancelConfirmBtn = new Button
            {
                Content = "Cancel",
                Style = defaultBtnStyle,
                FontSize = 11,
                MinHeight = 26,
                Padding = new Thickness(10, 2, 10, 2)
            };
            confirmRow.Children.Add(removeConfirmBtn);
            confirmRow.Children.Add(cancelConfirmBtn);
            confirmPanel.Children.Add(confirmRow);
            confirmFlyout.Content = confirmPanel;

            removeConfirmBtn.Click += (_, _) =>
            {
                confirmFlyout.Hide();
                var rowVm = ViewModel.FindGameRow(entry.GameName);
                if (rowVm is not null)
                {
                    ViewModel.RemoveRows([rowVm]);
                }

                adjustSummaryAfterRemove();
                hostPanel.Children.Remove(block);
                _ = ShowStatusToastAsync($"Removed “{entry.GameName}” from your list.");
            };

            cancelConfirmBtn.Click += (_, _) => confirmFlyout.Hide();

            noBtn.Click += (_, _) =>
            {
                if (XamlRoot is not null)
                {
                    confirmFlyout.XamlRoot = XamlRoot;
                }

                confirmFlyout.ShowAt(noBtn);
            };

            block.Children.Add(btnGrid);
        }

        return block;
    }

    private void TryOpenFolderInExplorer(string pathRaw)
    {
        try
        {
            var path = Path.GetFullPath(pathRaw.Trim());
            if (!Directory.Exists(path))
            {
                _ = ShowStatusToastAsync("That save folder is not reachable from here.");
                return;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            _ = ShowStatusToastAsync($"Could not open folder: {ex.Message}");
        }
    }

    private static TextBlock AddStatRow(StackPanel parent, string label, string value, Brush valueBrush, Brush labelBrush)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "• " + label, FontSize = 11, Foreground = labelBrush });
        var valueTb = new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = valueBrush,
            FontWeight = FontWeights.SemiBold
        };
        row.Children.Add(valueTb);
        parent.Children.Add(row);
        return valueTb;
    }

    private Brush ResolveShellDialogBrush(string key, Brush fallback)
    {
        if (!string.IsNullOrEmpty(key) && key.StartsWith("Gsbt", StringComparison.Ordinal))
        {
            return ThemeBridge.GetGsbtBrush(ThemeBridge.IsShellDarkTheme(), key);
        }

        return Resources.TryGetValue(key, out var o) && o is Brush b ? b : fallback;
    }
}
