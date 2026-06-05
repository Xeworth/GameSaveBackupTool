using System.Runtime.InteropServices;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace GSBT.WinUI.Services;

/// <summary>Windows taskbar progress (ITaskbarList3) and main-window title percentage during backup/compress.</summary>
internal static class MainWindowShellProgress
{
    private static string? _baseTitle;
    private static bool _operationActive;
    private static bool _flashOnIdle;

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

        var hwnd = WindowNative.GetWindowHandle(window);
        var isBackup = viewModel.FooterBackupShowsCancel;
        var isCompress = viewModel.FooterCompressShowsCancel;
        var showOp = isBackup || isCompress;

        if (showOp)
        {
            _operationActive = true;
            _flashOnIdle = true;
            var pct = (int)Math.Round(Math.Clamp(viewModel.ScanProgress, 0, 100));
            TaskbarProgressInterop.SetNormal(hwnd, (ulong)pct, 100);
            var verb = isCompress ? "Compressing" : "Backing up";
            window.Title = $"{_baseTitle} — {verb} {pct}%";
            return;
        }

        TaskbarProgressInterop.Clear(hwnd);
        RestoreTitle(window);

        if (!viewModel.IsBusy && _operationActive)
        {
            _operationActive = false;
            if (_flashOnIdle)
            {
                _flashOnIdle = false;
                TaskbarProgressInterop.Flash(hwnd);
            }
        }
    }

    public static void Clear(Window? window)
    {
        if (window is null)
        {
            return;
        }

        _operationActive = false;
        _flashOnIdle = false;
        try
        {
            TaskbarProgressInterop.Clear(WindowNative.GetWindowHandle(window));
            RestoreTitle(window);
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

        try
        {
            window.Title = _baseTitle;
        }
        catch (COMException)
        {
            // Window may already be closed during MainPage unload/shutdown.
        }
    }
}

internal static class TaskbarProgressInterop
{
    private static ITaskbarList3? _taskbar;
    private static bool _initAttempted;

    public static void SetNormal(IntPtr hwnd, ulong completed, ulong total)
    {
        if (!TryEnsure() || hwnd == IntPtr.Zero)
        {
            return;
        }

        _taskbar!.SetProgressState(hwnd, TaskbarProgressBarState.Normal);
        _taskbar.SetProgressValue(hwnd, completed, total);
    }

    public static void Clear(IntPtr hwnd)
    {
        if (!TryEnsure() || hwnd == IntPtr.Zero)
        {
            return;
        }

        _taskbar!.SetProgressState(hwnd, TaskbarProgressBarState.NoProgress);
    }

    /// <summary>Flash taskbar button when the window is not in the foreground.</summary>
    public static void Flash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || NativeMethods.GetForegroundWindow() == hwnd)
        {
            return;
        }

        var info = new NativeMethods.Flashwinfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.Flashwinfo>(),
            Hwnd = hwnd,
            DwFlags = NativeMethods.FlashwTray | NativeMethods.FlashwTimernofg,
            UCount = 4,
            DwTimeout = 0,
        };
        _ = NativeMethods.FlashWindowEx(ref info);
    }

    private static bool TryEnsure()
    {
        if (_taskbar is not null)
        {
            return true;
        }

        if (_initAttempted)
        {
            return false;
        }

        _initAttempted = true;
        try
        {
            _taskbar = (ITaskbarList3)new CTaskbarList();
            _taskbar.HrInit();
            return true;
        }
        catch
        {
            _taskbar = null;
            return false;
        }
    }

    private enum TaskbarProgressBarState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList;

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a442efe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TaskbarProgressBarState tbpFlags);
    }

    private static class NativeMethods
    {
        internal const uint FlashwTray = 0x00000002;
        internal const uint FlashwTimernofg = 0x0000000C;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlashWindowEx(ref Flashwinfo pfwi);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Flashwinfo
        {
            public uint CbSize;
            public IntPtr Hwnd;
            public uint DwFlags;
            public uint UCount;
            public uint DwTimeout;
        }
    }
}
