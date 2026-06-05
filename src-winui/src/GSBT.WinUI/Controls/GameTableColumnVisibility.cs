using GSBT.WinUI.Services;

namespace GSBT.WinUI.Controls;

/// <summary>Persisted visibility for optional game-table columns.</summary>
public static class GameTableColumnVisibility
{
    public const string ShowPlatformColumnKey = "table_show_platform_column";
    public const string ShowBackupSizeColumnKey = "table_show_backup_size_column";

    public static bool IsColumnVisible(GameTableColumn column, SettingsStore? store)
    {
        if (string.IsNullOrWhiteSpace(column.VisibilitySettingsKey))
        {
            return true;
        }

        var fallback = true;
        return store?.Get(column.VisibilitySettingsKey, fallback) ?? fallback;
    }

    public static IReadOnlyList<GameTableColumn> FilterVisibleColumns(
        IReadOnlyList<GameTableColumn> definitions,
        SettingsStore? store) =>
        definitions.Where(c => IsColumnVisible(c, store)).ToList();
}
