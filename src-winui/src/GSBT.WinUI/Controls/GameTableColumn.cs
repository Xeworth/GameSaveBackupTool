using GSBT.WinUI.ViewModels;

namespace GSBT.WinUI.Controls;

/// <summary>
/// Declares one logical column: header text, sizing rules, and how to read cell text from <see cref="GameRowViewModel"/>.
/// Add entries only to <see cref="GameTableColumns.Definitions"/> — header, row cells, and resize logic follow automatically.
/// </summary>
public sealed class GameTableColumn
{
    /// <summary>Stable id for persistence or diagnostics.</summary>
    public required string Id { get; init; }

    public required string Header { get; init; }

    /// <summary>When true, this column uses <see cref="GridUnitType.Star"/> and fills space left after fixed columns.</summary>
    public bool IsStarColumn { get; init; }

    /// <summary>Initial width for fixed columns; ignored when <see cref="IsStarColumn"/> is true except as a hint.</summary>
    public double InitialPixelWidth { get; init; }

    public double MinPixelWidth { get; init; } = 80;

    public double MaxPixelWidth { get; init; } = 800;

    /// <summary>When set, column can be hidden via <see cref="GameTableColumnVisibility"/> settings.</summary>
    public string? VisibilitySettingsKey { get; init; }

    public required Func<GameRowViewModel, string> GetText { get; init; }
}
