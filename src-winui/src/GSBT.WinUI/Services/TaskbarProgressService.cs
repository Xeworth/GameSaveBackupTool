using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace GSBT.WinUI.Services;

/// <summary>Windows taskbar progress overlay (requires HWND; works with bundled native 7z progress).</summary>
internal static class TaskbarProgressService
{
    private static ITaskbarList3? _taskbarList;
    private static int _lastPct = -1;
    private static TaskbarProgressState _lastState = TaskbarProgressState.NoProgress;

    public static void Sync(Window? window, int percent, bool active, bool cancelRequested)
    {
        if (window is null || !active)
        {
            Clear(window);
            return;
        }

        var taskbar = EnsureTaskbarList();
        if (taskbar is null)
        {
            return;
        }

        var pct = Math.Clamp(percent, 0, 100);
        var state = cancelRequested
            ? TaskbarProgressState.Paused
            : pct >= 100
                ? TaskbarProgressState.NoProgress
                : TaskbarProgressState.Normal;
        if (pct == _lastPct && state == _lastState)
        {
            return;
        }

        _lastPct = pct;
        _lastState = state;
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (state == TaskbarProgressState.NoProgress)
            {
                taskbar.SetProgressState(hwnd, TaskbarProgressState.NoProgress);
                return;
            }

            taskbar.SetProgressState(hwnd, state);
            taskbar.SetProgressValue(hwnd, (ulong)pct, 100);
        }
        catch (COMException)
        {
            // Window may already be closed during unload/shutdown.
        }
    }

    public static void Clear(Window? window)
    {
        _lastPct = -1;
        _lastState = TaskbarProgressState.NoProgress;
        var taskbar = EnsureTaskbarList();
        if (taskbar is null || window is null)
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            taskbar.SetProgressState(hwnd, TaskbarProgressState.NoProgress);
        }
        catch (COMException)
        {
            // ignore
        }
    }

    private static ITaskbarList3? EnsureTaskbarList()
    {
        if (_taskbarList is not null)
        {
            return _taskbarList;
        }

        try
        {
            var taskbar = (ITaskbarList3)new CTaskbarList();
            taskbar.HrInit();
            _taskbarList = taskbar;
            return _taskbarList;
        }
        catch
        {
            return null;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
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
        void SetProgressState(IntPtr hwnd, TaskbarProgressState tbpFlags);
    }

    private enum TaskbarProgressState : uint
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }
}
