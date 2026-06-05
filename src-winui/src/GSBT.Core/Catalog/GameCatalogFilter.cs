namespace GSBT.Core.Catalog;

/// <summary>
/// Filter modes for the main game table (Python <c>filter_state</c> parity: all / found / not found).
/// </summary>
public enum GameCatalogFilterMode
{
    All,
    FoundOnly,
    NotFoundOnly
}

/// <summary>
/// Centralized filter rules so WinUI and tests stay aligned. "Found" = on-disk path or registry-only save metadata.
/// </summary>
public static class GameCatalogFilter
{
    /// <summary>Returns true when the row represents a located save (folder path or in-registry-only).</summary>
    public static bool HasSaveLocation(string? resolvedPath, bool registryOnly)
        => registryOnly || !string.IsNullOrWhiteSpace(resolvedPath);

    public static bool IncludeRow(GameCatalogFilterMode mode, bool hasSaveLocation) =>
        mode switch
        {
            GameCatalogFilterMode.All => true,
            GameCatalogFilterMode.FoundOnly => hasSaveLocation,
            GameCatalogFilterMode.NotFoundOnly => !hasSaveLocation,
            _ => true
        };
}
