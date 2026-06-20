using System.Runtime.InteropServices;

namespace GSBT.WinUI.Services;

/// <summary>Non-client chrome measured from the live window (used for iconic restore bounds).</summary>
internal static class WindowFrameMetrics
{
    private const int DefaultFrameWidth = 16;
    private const int DefaultFrameHeight = 39;

    private static int _frameWidth = DefaultFrameWidth;
    private static int _frameHeight = DefaultFrameHeight;

    public static (int Width, int Height) FrameInsets => (_frameWidth, _frameHeight);

    public static void NoteVisibleWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd))
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)
            || !NativeMethods.GetClientRect(hwnd, out var clientRect))
        {
            return;
        }

        var frameW = (windowRect.Right - windowRect.Left) - (clientRect.Right - clientRect.Left);
        var frameH = (windowRect.Bottom - windowRect.Top) - (clientRect.Bottom - clientRect.Top);
        if (frameW > 0 && frameH > 0)
        {
            _frameWidth = frameW;
            _frameHeight = frameH;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
