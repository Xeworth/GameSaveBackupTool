using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Controls;

/// <summary>Tag-based helpers for settings and sandbox <see cref="ComboBox"/> pickers.</summary>
internal static class ComboBoxTagExtensions
{
    public static void AddOption(this ComboBox combo, string label, object tag) =>
        combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });

    public static void SetSelectedTag(this ComboBox combo, object? tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (TagsEqual(item.Tag, tag))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    public static string GetSelectedStringTag(this ComboBox combo, string fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? fallback;

    public static int GetSelectedIntTag(this ComboBox combo, int fallback) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag switch
        {
            int i => i,
            long l => (int)l,
            _ => fallback,
        };

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
}
