using GSBT.Core.Models;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Views;

public sealed partial class MainPage
{
    private async Task RunRestoreWorkflowAsync(GameRowViewModel row)
    {
        var snapshots = ViewModel.GetRestoreSnapshots(row);
        if (snapshots.Count == 0)
        {
            await ShowStatusToastAsync("No retained backup snapshot is available for this game.");
            return;
        }

        var snapshotBox = new ComboBox
        {
            Header = "Snapshot",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = snapshots,
            DisplayMemberPath = nameof(RestoreSnapshotOption.DisplayText),
            SelectedIndex = 0,
        };
        var modeBox = new ComboBox
        {
            Header = "Restore mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Replace live save", "Merge with live save", "Restore to another folder" },
            SelectedIndex = 0,
            Visibility = row.SaveInRegistryOnly ? Visibility.Collapsed : Visibility.Visible,
        };
        var targetBox = new TextBox
        {
            Header = "Alternate folder",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
        };
        var browse = new Button
        {
            Content = "Browse...",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        browse.Click += async (_, _) =>
        {
            var selected = await PickFolderWithPickerAsync();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                targetBox.Text = selected;
            }
        };
        modeBox.SelectionChanged += (_, _) =>
        {
            var alternate = modeBox.SelectedIndex == 2;
            targetBox.Visibility = alternate ? Visibility.Visible : Visibility.Collapsed;
            browse.Visibility = alternate ? Visibility.Visible : Visibility.Collapsed;
        };

        var choiceContent = new StackPanel { Spacing = 10, MinWidth = 430 };
        choiceContent.Children.Add(new TextBlock
        {
            Text = "GSBT verifies the snapshot first and creates a pre-restore safety copy before replacing live data.",
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        choiceContent.Children.Add(snapshotBox);
        choiceContent.Children.Add(modeBox);
        choiceContent.Children.Add(targetBox);
        choiceContent.Children.Add(browse);
        var choice = new ContentDialog
        {
            Title = $"Restore {row.GameName}",
            Content = choiceContent,
            PrimaryButtonText = "Preview",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        ApplyShellThemeToContentDialog(choice);
        if (await GsbtContentDialog.ShowAsync(choice) != ContentDialogResult.Primary
            || snapshotBox.SelectedItem is not RestoreSnapshotOption snapshot)
        {
            return;
        }

        var mode = modeBox.SelectedIndex switch
        {
            1 => RestoreMode.Merge,
            2 => RestoreMode.Alternate,
            _ => RestoreMode.Replace,
        };
        var plan = ViewModel.CreateRestorePlan(row, snapshot, mode, targetBox.Text);
        var previewContent = new StackPanel { Spacing = 6, MinWidth = 460 };
        previewContent.Children.Add(new TextBlock { Text = $"Snapshot: {plan.BackupRunPath}", TextWrapping = TextWrapping.WrapWholeWords });
        previewContent.Children.Add(new TextBlock { Text = $"Target: {plan.TargetPath}", TextWrapping = TextWrapping.WrapWholeWords });
        previewContent.Children.Add(new TextBlock { Text = $"Mode: {plan.Mode}" });
        previewContent.Children.Add(new TextBlock { Text = $"Files: {plan.FileCount:N0}    Conflicts: {plan.ConflictCount:N0}" });
        foreach (var warning in plan.Warnings)
        {
            previewContent.Children.Add(new TextBlock
            {
                Text = warning,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            });
        }

        foreach (var error in plan.Errors.Take(8))
        {
            previewContent.Children.Add(new TextBlock
            {
                Text = error,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            });
        }

        var confirm = new ContentDialog
        {
            Title = plan.IsValid ? "Confirm restore" : "Restore cannot continue",
            Content = previewContent,
            PrimaryButtonText = plan.IsValid ? "Restore" : string.Empty,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        ApplyShellThemeToContentDialog(confirm);
        if (!plan.IsValid || await GsbtContentDialog.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await ViewModel.ExecuteRestoreAsync(row, plan);
        await ShowStatusToastAsync(
            result.Success ? $"Restored {row.GameName}." : result.Error ?? "Restore failed.",
            result.Success ? 5000 : 8000,
            severity: result.Success ? BackupToastSeverity.Neutral : BackupToastSeverity.Error);
    }
}
