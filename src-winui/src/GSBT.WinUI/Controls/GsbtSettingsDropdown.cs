using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GSBT.WinUI.Controls;

/// <summary>
/// Settings pickers: <see cref="Button"/> + <see cref="MenuFlyout"/> (footer menu chrome, opens downward).
/// </summary>
public sealed class GsbtSettingsDropdown : UserControl
{
    private readonly Button _button;
    private readonly MenuFlyout _flyout;
    private readonly TextBlock _selectedLabel;
    private readonly List<GsbtSettingsDropdownOption> _options = new();
    private object? _selectedTag;

    public GsbtSettingsDropdown()
    {
        _selectedLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        FontFamily? chevronFont = null;
        if (Application.Current.Resources.TryGetValue("SymbolThemeFontFamily", out var symFf) && symFf is FontFamily ff)
        {
            chevronFont = ff;
        }

        var chevron = new FontIcon
        {
            Glyph = "\uE70D",
            FontSize = 11,
            FontFamily = chevronFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20), MinWidth = 20 });
        Grid.SetColumn(_selectedLabel, 0);
        Grid.SetColumn(chevron, 1);
        row.Children.Add(_selectedLabel);
        row.Children.Add(chevron);

        _flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        GsbtMenuFlyoutChrome.ApplyToFlyout(_flyout);

        _button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            MinHeight = UiMetrics.SettingsDropdownMinHeight,
            Padding = new Thickness(10, 4, 12, 4),
            Style = Application.Current.Resources["DefaultButtonStyle"] as Style,
            Flyout = _flyout,
        };

        VerticalAlignment = VerticalAlignment.Center;
        Content = _button;
    }

    public event EventHandler<GsbtSettingsDropdownSelectionChangedEventArgs>? SelectionChanged;

    public object? SelectedTag => _selectedTag;

    public new bool IsEnabled
    {
        get => _button.IsEnabled;
        set => _button.IsEnabled = value;
    }

    public void AddOption(string label, object tag)
    {
        _options.Add(new GsbtSettingsDropdownOption(label, tag));
        var item = new MenuFlyoutItem { Text = label, Tag = tag };
        GsbtMenuFlyoutChrome.ApplyToItem(item);
        item.Click += MenuItem_Click;
        _flyout.Items.Add(item);

        if (_selectedTag is null)
        {
            SetSelectedTag(tag, raiseEvent: false);
        }
    }

    public void SetSelectedTag(object? tag, bool raiseEvent = false)
    {
        var match = _options.FirstOrDefault(o => TagsEqual(o.Tag, tag));
        if (match is null && tag is not null)
        {
            return;
        }

        _selectedTag = match?.Tag ?? tag;
        _selectedLabel.Text = match?.Label ?? _options.FirstOrDefault()?.Label ?? string.Empty;
        if (raiseEvent)
        {
            SelectionChanged?.Invoke(this, new GsbtSettingsDropdownSelectionChangedEventArgs(_selectedTag));
        }
    }

    public string GetSelectedStringTag(string fallback) =>
        SelectedTag as string ?? fallback;

    public int GetSelectedIntTag(int fallback) =>
        SelectedTag switch
        {
            int i => i,
            long l => (int)l,
            _ => fallback,
        };

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is null)
        {
            return;
        }

        if (TagsEqual(_selectedTag, item.Tag))
        {
            return;
        }

        SetSelectedTag(item.Tag, raiseEvent: false);
        SelectionChanged?.Invoke(this, new GsbtSettingsDropdownSelectionChangedEventArgs(_selectedTag));
    }

    private static bool TagsEqual(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a is string sa && b is string sb)
        {
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        }

        if (a is int ia && b is int ib)
        {
            return ia == ib;
        }

        return Equals(a, b);
    }

    private sealed record GsbtSettingsDropdownOption(string Label, object Tag);
}

public sealed class GsbtSettingsDropdownSelectionChangedEventArgs : EventArgs
{
    public GsbtSettingsDropdownSelectionChangedEventArgs(object? selectedTag) => SelectedTag = selectedTag;

    public object? SelectedTag { get; }
}
