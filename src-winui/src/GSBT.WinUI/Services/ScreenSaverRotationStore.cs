using System.Text.Json;
using GSBT.Core.Common;

namespace GSBT.WinUI.Services;

/// <summary>Round-robin screen saver asset selection persisted between sessions.</summary>
internal static class ScreenSaverRotationStore
{
    private sealed record PersistedState(int LastPlayedId);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ScreenSaverAssetSet PickNext(int? forceAssetId = null)
    {
        if (forceAssetId is > 0
            && ScreenSaverAssetCatalog.TryGetById(forceAssetId.Value, out var forced)
            && ScreenSaverAssetCatalog.AssetsExist(forced))
        {
            NotePlayed(forced.Id);
            return forced;
        }

        var available = ScreenSaverAssetCatalog.AvailableSets();
        if (available.Count == 0)
        {
            return ScreenSaverAssetCatalog.Default;
        }

        if (available.Count == 1)
        {
            NotePlayed(available[0].Id);
            return available[0];
        }

        var last = Load().LastPlayedId;
        var lastIndex = -1;
        for (var i = 0; i < available.Count; i++)
        {
            if (available[i].Id == last)
            {
                lastIndex = i;
                break;
            }
        }

        var next = lastIndex < 0
            ? available[0]
            : available[(lastIndex + 1) % available.Count];
        NotePlayed(next.Id);
        return next;
    }

    private static void NotePlayed(int id)
    {
        try
        {
            var dir = UserDataDir.GetWinUiUserDataDir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "screen_saver_rotation.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new PersistedState(id), JsonOptions));
        }
        catch
        {
            // best-effort rotation bookkeeping
        }
    }

    private static PersistedState Load()
    {
        try
        {
            var path = Path.Combine(UserDataDir.GetWinUiUserDataDir(), "screen_saver_rotation.json");
            if (!File.Exists(path))
            {
                return new PersistedState(0);
            }

            return JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(path)) ?? new PersistedState(0);
        }
        catch
        {
            return new PersistedState(0);
        }
    }
}
