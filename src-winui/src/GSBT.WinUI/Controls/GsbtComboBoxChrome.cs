using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GSBT.WinUI.Controls;

/// <summary>
/// Classic dropdown behavior for <see cref="ComboBox"/> (list opens below the control).
/// Main-window footer <see cref="MenuFlyout"/>s are configured separately and are not affected.
/// </summary>
public static class GsbtComboBoxChrome
{
    public static readonly DependencyProperty ClassicDropdownProperty =
        DependencyProperty.RegisterAttached(
            "ClassicDropdown",
            typeof(bool),
            typeof(GsbtComboBoxChrome),
            new PropertyMetadata(false, OnClassicDropdownChanged));

    public static bool GetClassicDropdown(DependencyObject element) =>
        (bool)element.GetValue(ClassicDropdownProperty);

    public static void SetClassicDropdown(DependencyObject element, bool value) =>
        element.SetValue(ClassicDropdownProperty, value);

    private static void OnClassicDropdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComboBox combo && e.NewValue is true)
        {
            Attach(combo);
        }
    }

    /// <summary>Wires classic downward popup placement for a combo box instance.</summary>
    public static void Attach(ComboBox combo)
    {
        combo.Loaded -= Combo_Loaded;
        combo.Loaded += Combo_Loaded;
        combo.DropDownOpened -= Combo_DropDownOpened;
        combo.DropDownOpened += Combo_DropDownOpened;

        if (combo is GsbtComboBox gsbt)
        {
            combo.PointerEntered -= GsbtCombo_PointerEntered;
            combo.PointerEntered += GsbtCombo_PointerEntered;
            if (combo.IsLoaded)
            {
                gsbt.SetArrowCursor();
            }
        }

        if (combo.IsLoaded)
        {
            ApplyPopupChrome(combo);
        }
    }

    internal static void ApplyPopupChrome(ComboBox combo)
    {
        if (FindDescendant<Popup>(combo) is { } popup)
        {
            popup.ShouldConstrainToRootBounds = false;
            popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        }
    }

    private static void Combo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo)
        {
            ApplyPopupChrome(combo);
            if (combo is GsbtComboBox gsbt)
            {
                gsbt.SetArrowCursor();
            }
        }
    }

    private static void Combo_DropDownOpened(object? sender, object e)
    {
        if (sender is ComboBox combo)
        {
            ApplyPopupChrome(combo);
        }
    }

    private static void GsbtCombo_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is GsbtComboBox gsbt)
        {
            gsbt.SetArrowCursor();
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
