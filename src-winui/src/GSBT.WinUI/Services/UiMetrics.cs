using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Services;

/// <summary>Shared layout constants for WinUI surfaces (insets, button chrome).</summary>
public static class UiMetrics
{
    /// <summary>Padding from window/client edges to labels, fields, tables (global).</summary>
    public const double WindowContentInset = 16;

    /// <summary>Horizontal padding inside primary/secondary buttons (toolbar + dialogs).</summary>
    public const double ButtonPaddingHorizontal = 10;

    /// <summary>Vertical padding paired with <see cref="CommandBarButtonMinHeight"/> for compact toolbar buttons.</summary>
    public const double ButtonPaddingVerticalCompact = 4;

    /// <summary>Toolbar / footer control strip height target (SplitButton, DropDownButton, Button).</summary>
    public const double CommandBarButtonMinHeight = 28;

    /// <summary>Settings MenuFlyout pickers: DefaultButtonStyle border needs a few px above toolbar height.</summary>
    public const double SettingsDropdownMinHeight = 32;

    /// <summary>Inset from window edge to the main footer toolbar content (status + buttons).</summary>
    public const double CommandBarInset = 10;

    /// <summary>Vertical gap between main form fields and OK/Cancel row in dialogs.</summary>
    public const double ContentToFooterButtonsGap = 16;

    /// <summary>Main window footer strip: vertical padding around status + button rows.</summary>
    public const double CommandBarPaddingVertical = 10;

    /// <summary>Tray context menu: even inset between flyout chrome and items (presenter only).</summary>
    public const double TrayMenuOuterPadding = 3;

    /// <summary>Tray / footer menu item inner padding (even X and Y).</summary>
    public const double TrayMenuItemPadding = 6;

    /// <summary>Tray / footer menu item vertical padding (paired with <see cref="TrayMenuItemPadding"/>).</summary>
    public const double TrayMenuItemPaddingVertical = 3;

    /// <summary>Match footer command-bar height (WinUI default menu items are 32px).</summary>
    public static void ApplyCompactMenuFlyoutItem(MenuFlyoutItem item)
    {
        item.MinHeight = 32;
        item.FontSize = 12;
        item.Padding = new Microsoft.UI.Xaml.Thickness(
            TrayMenuItemPadding,
            TrayMenuItemPaddingVertical,
            TrayMenuItemPadding,
            TrayMenuItemPaddingVertical);
    }
}
