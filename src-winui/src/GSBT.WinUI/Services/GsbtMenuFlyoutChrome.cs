using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Services;

/// <summary>Shared tray / footer menu flyout chrome: padded presenter + rounded item hover.</summary>
public static class GsbtMenuFlyoutChrome
{
    private static Style? _presenterStyle;
    private static Style? _itemStyle;

    public static Style PresenterStyle => _presenterStyle ??= ResolveStyle("GsbtMenuFlyoutPresenterStyle");

    public static Style ItemStyle => _itemStyle ??= ResolveStyle("GsbtMenuFlyoutItemStyle");

    public static void ApplyToFlyout(MenuFlyout flyout)
    {
        flyout.MenuFlyoutPresenterStyle = PresenterStyle;
        foreach (var item in flyout.Items)
        {
            if (item is MenuFlyoutItem mfi)
            {
                ApplyToItem(mfi);
            }
        }
    }

    public static void ApplyToItem(MenuFlyoutItem item) => item.Style = ItemStyle;

    private static Style ResolveStyle(string key)
    {
        if (Application.Current.Resources[key] is Style style)
        {
            return style;
        }

        throw new InvalidOperationException($"Missing application resource '{key}'.");
    }
}
