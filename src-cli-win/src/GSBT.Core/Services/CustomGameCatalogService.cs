using GSBT.Core.Common;

namespace GSBT.Core.Services;

/// <summary>Adds user-defined games to the catalog (WinUI <c>TryAddCustomGameAsync</c> parity).</summary>
public static class CustomGameCatalogService
{
    public static (bool Ok, string Message) AddFolderGame(
        SaveCatalogManager catalogManager,
        string rawName,
        string saveFolderRaw)
    {
        var name = (rawName ?? string.Empty).Trim();
        if (!GameNameInputValidation.IsValidGameNameForStorage(name, out var nameErr))
        {
            return (false, nameErr ?? "Enter a game name.");
        }

        var folderInput = (saveFolderRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folderInput))
        {
            return (false, "Choose a save folder.");
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(catalogManager.ResolvePath(folderInput, null) ?? folderInput);
        }
        catch
        {
            return (false, "That folder path is not valid.");
        }

        if (!Directory.Exists(resolved))
        {
            return (false, "That folder does not exist or is not reachable.");
        }

        var displayName = GameDisplayName.CleanDisplayName(name);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (false, "Enter a printable game name.");
        }

        if (!GameNameInputValidation.IsValidGameNameForStorage(displayName, out var displayErr))
        {
            return (false, displayErr ?? "Invalid game name.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["scan_outcome"] = "SAVE_ON_DISK",
            ["save_path"] = folderInput,
            ["platform"] = "Custom",
            [CatalogUserAdded.JsonPropertyName] = true,
        };

        catalogManager.AddOrUpdate(displayName, payload);
        catalogManager.Flush();

        return (true, $"Added \"{displayName}\".");
    }
}
