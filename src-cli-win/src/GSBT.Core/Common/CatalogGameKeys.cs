using GSBT.Core.Models;

namespace GSBT.Core.Common;

/// <summary>
/// Canonical key for matching <see cref="GameRecord"/> to persisted catalog rows (<see cref="Models.SaveScanResult"/> names).
/// </summary>
public static class CatalogGameKeys
{
    /// <summary>
    /// Same naming rule as <c>SaveScanResult.Name</c> produced by <c>ScanService.ProcessSingleGame</c>.
    /// </summary>
    public static string FromDetectedGame(GameRecord game) =>
        game.CatalogDisplayName ?? GameDisplayName.CleanDisplayName(game.Name);
}
