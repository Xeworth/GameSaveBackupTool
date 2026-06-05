namespace GSBT.WinUI.Controls;

/// <summary>
/// Single registry for game table columns. To add a column: append one <see cref="GameTableColumn"/> here
/// (exactly one entry should use <see cref="GameTableColumn.IsStarColumn"/> = true).
/// </summary>
public static class GameTableColumns
{
    public static IReadOnlyList<GameTableColumn> Definitions { get; } =
    [
        new GameTableColumn
        {
            Id = "gameName",
            Header = "Game Name",
            IsStarColumn = true,
            InitialPixelWidth = 400,
            MinPixelWidth = 160,
            MaxPixelWidth = 1200,
            GetText = r => r.GameName
        },
        new GameTableColumn
        {
            Id = "saveStatus",
            Header = "Save Status",
            IsStarColumn = false,
            InitialPixelWidth = 90,
            MinPixelWidth = 90,
            MaxPixelWidth = 90,
            GetText = r => r.SaveStatus
        },
        new GameTableColumn
        {
            Id = "platform",
            Header = "Platform",
            IsStarColumn = false,
            InitialPixelWidth = 90,
            MinPixelWidth = 90,
            MaxPixelWidth = 90,
            VisibilitySettingsKey = GameTableColumnVisibility.ShowPlatformColumnKey,
            GetText = r => FormatPlatformCell(r.Platform)
        },
        new GameTableColumn
        {
            Id = "backupSize",
            Header = "Backup Size",
            IsStarColumn = false,
            InitialPixelWidth = 90,
            MinPixelWidth = 90,
            MaxPixelWidth = 90,
            VisibilitySettingsKey = GameTableColumnVisibility.ShowBackupSizeColumnKey,
            GetText = r => r.BackupSizeDisplay
        },
        new GameTableColumn
        {
            Id = "lastBackup",
            Header = "Last Backup",
            IsStarColumn = false,
            InitialPixelWidth = 145,
            MinPixelWidth = 145,
            MaxPixelWidth = 145,
            GetText = r => r.LastBackup
        }
    ];

    /// <summary>Hides legacy/catalog placeholders so the column shows store/source names only (Steam, GOG, PC, …).</summary>
    private static string FormatPlatformCell(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return string.Empty;
        }

        if (string.Equals(platform, "Cached", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return platform;
    }
}
