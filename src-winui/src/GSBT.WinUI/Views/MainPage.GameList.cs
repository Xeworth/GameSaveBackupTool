using System.Collections.Generic;
using System.IO;
using System.Linq;
using GSBT.Core.Services;
using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace GSBT.WinUI.Views;

public sealed partial class MainPage
{
    private void WireGameTableContextMenu()
    {
        GamesTable.RowContextRequested += GamesTable_RowContextRequested;
        ViewModel.TeachingTipOnboardingScanAddRequested += ViewModel_OnboardingScanAddRequested;
    }

    private void UnwireGameTableContextMenu()
    {
        GamesTable.RowContextRequested -= GamesTable_RowContextRequested;
        ViewModel.TeachingTipOnboardingScanAddRequested -= ViewModel_OnboardingScanAddRequested;
    }

    private void ViewModel_OnboardingScanAddRequested(object? sender, EventArgs e)
    {
        try
        {
            OnboardingScanAddTeachingTip.Target = ScanAndCustomToolbarHost;
            OnboardingScanAddTeachingTip.IsOpen = true;
        }
        catch
        {
            // ignore tip failures
        }
    }

    private void OnboardingScanAddTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        ViewModel.MarkOnboardingScanAddTipDismissed();
    }

    private void CompressWorkflowTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        ViewModel.MarkCompressTeachingTipDismissed();
        _ = ScheduleSettingsAfterCompressTeachingTipAsync();
    }

    private void SettingsAfterCompressTeachingTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args) =>
        ViewModel.MarkSettingsAfterCompressTeachingTipDismissed();

    private async Task ScheduleSettingsAfterCompressTeachingTipAsync()
    {
        await Task.Delay(550);
        if (!ViewModel.ShouldShowSettingsAfterCompressTeachingTip())
        {
            return;
        }

        try
        {
            SettingsAfterCompressTeachingTip.Target = SettingsPageButton;
            SettingsAfterCompressTeachingTip.IsOpen = true;
        }
        catch
        {
            // ignore
        }
    }

    private async Task ScheduleOnboardingTeachingTipAsync()
    {
        await Task.Delay(750);
        ViewModel.RequestOnboardingScanAddTipIfNeeded();
    }

    private async Task ScheduleCompressTeachingTipAsync()
    {
        await Task.Delay(2600);
        if (!ViewModel.ShouldShowCompressTeachingTip())
        {
            return;
        }

        try
        {
            CompressWorkflowTeachingTip.Target = CompressButton;
            CompressWorkflowTeachingTip.IsOpen = true;
        }
        catch
        {
            // ignore
        }
    }

    private void GamesTable_RowContextRequested(object? sender, GameRowContextRequestedEventArgs e)
    {
        var vm = e.ViewModel;
        var flyout = new MenuFlyout();

        ViewModel.SyncLogicalSelectionFromVisibleGrid(GamesGrid.SelectedItems);
        var gridSelected = GamesGrid.SelectedItems.OfType<GameRowViewModel>().ToList();
        var batch = gridSelected.Count > 1 && gridSelected.Contains(vm);
        var targets = batch ? gridSelected : new List<GameRowViewModel> { vm };

        var backupLabel = batch ? $"Backup ({targets.Count} games)" : "Backup";
        var backup = new MenuFlyoutItem { Text = backupLabel };
        backup.IsEnabled = targets.Any(ViewModel.CanBackupRowForUi);
        backup.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.B, Modifiers = VirtualKeyModifiers.Control });
        backup.Click += async (_, _) =>
        {
            ViewModel.AlignLogicalSelectionTo(targets);
            GamesGrid.SelectedItems.Clear();
            foreach (var row in targets)
            {
                GamesGrid.SelectedItems.Add(row);
            }

            await RunManualBackupAsync();
        };
        flyout.Items.Add(backup);

        var deleteLabel = batch ? $"Delete ({targets.Count} games)" : "Delete";
        var delete = new MenuFlyoutItem { Text = deleteLabel };
        delete.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.Delete });
        delete.Click += (_, _) =>
        {
            ViewModel.RemoveRows(targets);
            foreach (var row in targets)
            {
                if (GamesGrid.SelectedItems.Contains(row))
                {
                    GamesGrid.SelectedItems.Remove(row);
                }
            }
        };
        flyout.Items.Add(delete);

        var secondary = new List<MenuFlyoutItem>();
        if (!batch)
        {
            var openSave = new MenuFlyoutItem { Text = "Open save folder" };
            openSave.IsEnabled = vm.HasSaveLocation
                && !vm.SaveInRegistryOnly
                && !string.IsNullOrWhiteSpace(vm.SavePathResolved)
                && Directory.Exists(vm.SavePathResolved);
            openSave.Click += (_, _) =>
            {
                var (ok, path, err) = ViewModel.TryGetOpenSaveFolderPath(vm);
                if (!ok)
                {
                    _ = ShowStatusToastAsync(err ?? "Could not open save folder.");
                    return;
                }

                TryOpenFolderInExplorer(path!);
            };
            secondary.Add(openSave);

            var openBackup = new MenuFlyoutItem { Text = "Open backup folder" };
            openBackup.IsEnabled = ViewModel.HasConfiguredBackupDestination();
            openBackup.Click += (_, _) =>
            {
                var (ok, path, hint) = ViewModel.TryGetOpenGameBackupsFolderPath(vm);
                if (!ok)
                {
                    _ = ShowStatusToastAsync(hint ?? "Could not open backup folder.");
                    return;
                }

                TryOpenFolderInExplorer(path!);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    _ = ShowStatusToastAsync(hint);
                }
            };
            secondary.Add(openBackup);

            if (vm.IsUserAdded)
            {
                var rename = new MenuFlyoutItem { Text = "Rename…" };
                rename.Click += async (_, _) =>
                {
                    var (ok, newName) = await ShowRenameGameDialogAsync(vm.GameName);
                    if (!ok)
                    {
                        return;
                    }

                    var (renOk, msg) = await ViewModel.TryRenameUserAddedGameAsync(vm, newName);
                    _ = ShowStatusToastAsync(msg);
                };
                secondary.Add(rename);
            }

            if (!vm.HasSaveLocation)
            {
                var addPath = new MenuFlyoutItem { Text = "Add save folder…" };
                addPath.Click += async (_, _) =>
                {
                    var path = await PickFolderWithPickerAsync();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return;
                    }

                    var (ok, msg) = await ViewModel.TryAssignSaveFolderForRowAsync(vm, path);
                    _ = ShowStatusToastAsync(msg);
                };
                secondary.Add(addPath);

                var addReg = new MenuFlyoutItem { Text = "Add registry path…" };
                addReg.Click += async (_, _) =>
                {
                    var (dlgOk, pasted) = await ShowAddRegistrySaveDialogAsync();
                    if (!dlgOk)
                    {
                        return;
                    }

                    var (ok, msg) = await ViewModel.TryAssignRegistrySaveForRowAsync(vm, pasted);
                    _ = ShowStatusToastAsync(msg);
                };
                secondary.Add(addReg);
            }
        }

        if (secondary.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            foreach (var item in secondary)
            {
                flyout.Items.Add(item);
            }
        }

        if (targets.Any(t => t.LastBackupIntegrityWarning))
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clearWarn = new MenuFlyoutItem { Text = "Clear Last backup highlights" };
            clearWarn.Click += (_, _) => ViewModel.ClearLastBackupIntegrityWarnings(targets);
            flyout.Items.Add(clearWarn);
        }

        flyout.ShowAt(e.PlacementTarget, new FlyoutShowOptions { Position = e.Position });
    }

    private async Task<string?> PickFolderWithPickerAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowRef);
            InitializeWithWindow.Initialize(picker, hwnd);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Backup toolbar, Ctrl+B, row context menu, and tray Backup share this flow.</summary>
    /// <param name="fromTray">When true, skip the informational estimate dialog if the window is hidden (severity warnings still show the dialog after restoring the window).</param>
    private async Task RunManualBackupAsync(bool fromTray = false)
    {
        if (!ViewModel.CanUseBackupAndCompress)
        {
            return;
        }

        if (!ViewModel.HasConfiguredBackupDestination())
        {
            var (ok, path, useDefault) = await ShowSelectBackupDestinationDialogAsync();
            if (!ok)
            {
                return;
            }

            ViewModel.PersistBackupDestinationFromPrompt(path, useDefault);
        }

        var selected = ViewModel.SnapshotLogicalSelection();
        var backupCandidates = ViewModel.GetBackupCandidates(selected);
        if (ViewModel.WarnBackupFolderNameCollisionsEnabled)
        {
            var collisions = ViewModel.GetSanitizedFolderCollisionWarnings(backupCandidates);
            if (collisions.Count > 0)
            {
                if (fromTray && App.MainWindowRef is Window collisionWin)
                {
                    MainWindowTrayVisibility.ShowAndActivate(collisionWin);
                }

                var proceedCollision = await ShowBackupFolderCollisionDialogAsync(collisions);
                if (!proceedCollision)
                {
                    ViewModel.StatusText = "Backup cancelled.";
                    _ = ShowStatusToastAsync("Backup cancelled.");
                    return;
                }
            }
        }

        ViewModel.StatusText = "Estimating backup size…";
        BackupSizeEstimateSummary? summary = null;
        try
        {
            summary = await ViewModel.ComputeBackupEstimateAsync(selected);
        }
        finally
        {
            if (ViewModel.StatusText == "Estimating backup size…")
            {
                ViewModel.StatusText = "Ready.";
            }
        }

        var estimateOn = ViewModel.BackupSizeEstimateEnabled;
        var mustShowEstimateDialog = summary is not null && (estimateOn || summary.HasSeverityWarnings);
        if (mustShowEstimateDialog && summary is not null)
        {
            var warningOnly = !estimateOn && summary.HasSeverityWarnings;
            bool proceed;
            if (fromTray && !summary.HasSeverityWarnings)
            {
                // Tray cannot show a modal over a hidden main window — run backup without the size summary dialog.
                proceed = true;
                _ = ShowStatusToastAsync(
                    "Starting backup from tray (size estimate prompt skipped).",
                    4500);
            }
            else
            {
                if (fromTray && App.MainWindowRef is Window win)
                {
                    MainWindowTrayVisibility.ShowAndActivate(win);
                }

                proceed = await ShowBackupEstimateConfirmDialogAsync(summary, warningOnly);
            }

            if (!proceed)
            {
                ViewModel.StatusText = "Backup cancelled.";
                _ = ShowStatusToastAsync("Backup cancelled.");
                return;
            }
        }

        var outcome = await ViewModel.BackupGamesAsync(selected);
        if (outcome.Failed > 0 && !outcome.Cancelled)
        {
            _ = ShowStatusToastAsync(
                outcome.Message,
                5000,
                AutoBackupToastChrome.None,
                "Check the status line or sandbox log for per-game errors.",
                BackupToastSeverity.Warning);
        }
        else
        {
            _ = ShowStatusToastAsync(outcome.Message);
        }

        OsAppNotifications.TryShow(
            _settingsStore,
            outcome.Cancelled ? "Backup cancelled" : "Backup",
            outcome.Message);

        if (outcome.Succeeded > 0 && ViewModel.ShouldShowCompressTeachingTip())
        {
            _ = ScheduleCompressTeachingTipAsync();
        }
    }
}
