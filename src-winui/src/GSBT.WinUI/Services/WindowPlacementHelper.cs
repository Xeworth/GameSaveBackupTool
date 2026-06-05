using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Foundation;
using Windows.Graphics;

namespace GSBT.WinUI.Services;

public enum NearAnchorPlacement
{
    /// <summary>Below the anchor; flip above if it would go off the bottom of the work area.</summary>
    PreferBelow,
    /// <summary>Above the anchor; flip below if it would go off the top of the work area.</summary>
    PreferAbove
}

/// <summary>Positions secondary windows near an anchor control or centered on the owner (screen / work-area clamped).</summary>
public static class WindowPlacementHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    /// <summary>
    /// Places <paramref name="child"/> near <paramref name="anchor"/> (below by default, or above when <paramref name="placement"/> requests it), or centered on <paramref name="owner"/> when <paramref name="anchor"/> is null.
    /// </summary>
    public static void PlaceWindowNearAnchor(Window child, Window owner, FrameworkElement? anchor, int marginDips = 8, NearAnchorPlacement placement = NearAnchorPlacement.PreferBelow)
    {
        try
        {
            if (anchor is null)
            {
                CenterOnOwner(child, owner);
                return;
            }

            var ownerContent = owner.Content as UIElement;
            if (ownerContent is null)
            {
                CenterOnOwner(child, owner);
                return;
            }

            var scale = anchor.XamlRoot?.RasterizationScale ?? 1.0;
            var gt = anchor.TransformToVisual(ownerContent);
            var bounds = gt.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));

            var ownerHwnd = WindowNative.GetWindowHandle(owner);
            var clientOrigin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(ownerHwnd, ref clientOrigin))
            {
                CenterOnOwner(child, owner);
                return;
            }

            var anchorLeftPx = clientOrigin.X + (int)Math.Round(bounds.X * scale);
            var anchorTopPx = clientOrigin.Y + (int)Math.Round(bounds.Y * scale);
            var anchorBottomPx = clientOrigin.Y + (int)Math.Round((bounds.Y + bounds.Height) * scale);
            var marginPx = (int)Math.Round(marginDips * scale);

            var childHwnd = WindowNative.GetWindowHandle(child);
            var childId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(childHwnd);
            var childApp = AppWindow.GetFromWindowId(childId);
            var w = childApp.Size.Width;
            var h = childApp.Size.Height;

            var targetX = anchorLeftPx;
            int targetY;

            if (placement == NearAnchorPlacement.PreferAbove)
            {
                targetY = anchorTopPx - h - marginPx;
                try
                {
                    var ownerId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
                    var display = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest);
                    var wa = display.WorkArea;
                    if (targetY < wa.Y)
                    {
                        targetY = anchorBottomPx + marginPx;
                    }
                }
                catch
                {
                    // keep targetY
                }
            }
            else
            {
                targetY = anchorBottomPx + marginPx;
                try
                {
                    var ownerId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
                    var display = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest);
                    var wa = display.WorkArea;

                    if (targetY + h > wa.Y + wa.Height)
                    {
                        targetY = anchorTopPx - h - marginPx;
                    }

                    if (targetY < wa.Y)
                    {
                        targetY = wa.Y;
                    }
                }
                catch
                {
                    // keep unclamped position
                }
            }

            try
            {
                var ownerId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
                var display = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest);
                var wa = display.WorkArea;

                if (targetY + h > wa.Y + wa.Height)
                {
                    targetY = wa.Y + wa.Height - h;
                }

                if (targetY < wa.Y)
                {
                    targetY = wa.Y;
                }

                if (targetX + w > wa.X + wa.Width)
                {
                    targetX = wa.X + wa.Width - w;
                }

                if (targetX < wa.X)
                {
                    targetX = wa.X;
                }
            }
            catch
            {
                // keep unclamped position
            }

            childApp.Move(new PointInt32(targetX, targetY));
        }
        catch
        {
            try
            {
                CenterOnOwner(child, owner);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>
    /// Places <paramref name="child"/> immediately to the right of <paramref name="owner"/> (same top edge),
    /// matching the owner's <see cref="AppWindow.Size"/>. Flips to the left when there is not enough work-area space.
    /// </summary>
    public static void PlaceWindowToRightOfOwner(Window child, Window owner, int gapDips = 8)
    {
        try
        {
            var ownerHwnd = WindowNative.GetWindowHandle(owner);
            var childHwnd = WindowNative.GetWindowHandle(child);
            var ownerApp = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd));
            var childApp = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(childHwnd));

            var ownerSize = ownerApp.Size;
            var ownerPos = ownerApp.Position;
            childApp.Resize(new SizeInt32(ownerSize.Width, ownerSize.Height));

            var scale = (owner.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            var gapPx = (int)Math.Round(gapDips * scale);
            var w = ownerSize.Width;
            var h = ownerSize.Height;

            var targetX = ownerPos.X + w + gapPx;
            var targetY = ownerPos.Y;

            try
            {
                var display = DisplayArea.GetFromWindowId(
                    Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd),
                    DisplayAreaFallback.Nearest);
                var wa = display.WorkArea;

                if (targetX + w > wa.X + wa.Width)
                {
                    targetX = ownerPos.X - w - gapPx;
                }

                if (targetX < wa.X)
                {
                    targetX = wa.X;
                }

                if (targetX + w > wa.X + wa.Width)
                {
                    targetX = wa.X + wa.Width - w;
                }

                if (targetY + h > wa.Y + wa.Height)
                {
                    targetY = wa.Y + wa.Height - h;
                }

                if (targetY < wa.Y)
                {
                    targetY = wa.Y;
                }
            }
            catch
            {
                // keep unclamped position
            }

            childApp.Move(new PointInt32(targetX, targetY));
        }
        catch
        {
            try
            {
                CenterOnOwner(child, owner);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void CenterOnOwner(Window child, Window owner)
    {
        var oh = WindowNative.GetWindowHandle(owner);
        var ch = WindowNative.GetWindowHandle(child);
        var oaw = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(oh));
        var caw = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ch));
        var ox = oaw.Position.X;
        var oy = oaw.Position.Y;
        var ow = oaw.Size.Width;
        var ohh = oaw.Size.Height;
        var cw = caw.Size.Width;
        var chh = caw.Size.Height;
        caw.Move(new PointInt32(ox + Math.Max(0, (ow - cw) / 2), oy + Math.Max(0, (ohh - chh) / 2)));
    }
}
