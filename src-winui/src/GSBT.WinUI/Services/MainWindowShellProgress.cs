using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace GSBT.WinUI.Services;

/// <summary>Main-window title percentage during backup/compress.</summary>
internal static class MainWindowShellProgress
{
    private static string? _baseTitle;
    private static int _lastTitlePct = -1;
    private static bool _lastCancelRequested;

    public static void CaptureBaseTitle(Window window)
    {
        if (string.IsNullOrWhiteSpace(_baseTitle))
        {
            _baseTitle = window.Title;
        }
    }

    public static void Sync(Window? window, MainViewModel viewModel)
    {
        if (window is null)
        {
            return;
        }

        CaptureBaseTitle(window);

        var isBackup = viewModel.FooterBackupShowsCancel;
        var isCompress = viewModel.FooterCompressShowsCancel;
        var showOp = isBackup || isCompress;

        if (showOp)
        {
            var pct = (int)Math.Round(Math.Clamp(viewModel.ScanProgress, 0, 100));
            UpdateOperationTitle(window, viewModel, isCompress, pct);
            TaskbarProgressService.Sync(window, pct, active: true, viewModel.OperationCancelRequested);
            return;
        }

        RestoreTitle(window);
        TaskbarProgressService.Clear(window);
        _lastTitlePct = -1;
        _lastCancelRequested = false;
    }

    public static void Clear(Window? window)
    {
        _lastTitlePct = -1;
        _lastCancelRequested = false;
        TaskbarProgressService.Clear(window);
        if (window is null)
        {
            return;
        }

        RestoreTitle(window);
    }

    private static void UpdateOperationTitle(Window window, MainViewModel viewModel, bool isCompress, int pct)
    {
        if (pct == _lastTitlePct && viewModel.OperationCancelRequested == _lastCancelRequested)
        {
            return;
        }

        _lastTitlePct = pct;
        _lastCancelRequested = viewModel.OperationCancelRequested;
        var verb = viewModel.OperationCancelRequested
            ? (isCompress ? "Canceling compress" : "Canceling backup")
            : (isCompress ? "Compressing" : "Backing up");
        SetShellTitle(window, $"{verb}... {pct}%");
    }

    private static void SetShellTitle(Window window, string title)
    {
        try
        {
            window.Title = title;
        }
        catch (COMException)
        {
            // Window may already be closed during MainPage unload/shutdown.
        }
    }

    private static void RestoreTitle(Window window)
    {
        if (string.IsNullOrWhiteSpace(_baseTitle))
        {
            return;
        }

        SetShellTitle(window, _baseTitle);
    }
}
