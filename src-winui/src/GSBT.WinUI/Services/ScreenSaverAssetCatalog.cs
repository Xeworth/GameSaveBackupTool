namespace GSBT.WinUI.Services;

/// <summary>Maps screen saver asset IDs to bundled video, audio, and progress-bar theme keys.</summary>
internal sealed record ScreenSaverAssetSet(
    int Id,
    string VideoFileName,
    string AudioFileName,
    string ProgressThemeKey,
    int VideoWidth,
    int VideoHeight);

internal static class ScreenSaverAssetCatalog
{
    public const double AudioBaseVolume = 0.20;

    /// <summary>Sandbox fake compress: fire after this many seconds below progress cap.</summary>
    public const int SimulationTriggerSeconds = 5;

    public const int TriggerMaxProgressPercent = 85;

    private static readonly ScreenSaverAssetSet[] Sets =
    [
        new(1, "videoID1.mp4", "audioID1.ogg", "water-blue", 854, 480),
        new(2, "videoID2.mp4", "audioID2.ogg", "sunset-glow", 854, 480),
        new(3, "videoID3.mp4", "audioID3.ogg", "forest-green", 854, 480),
        new(4, "videoID4.mp4", "audioID4.ogg", "bloom-pink", 854, 480),
    ];

    public static ScreenSaverAssetSet Default => Sets[0];

    public static IReadOnlyList<ScreenSaverAssetSet> AvailableSets()
    {
        var list = new List<ScreenSaverAssetSet>(Sets.Length);
        foreach (var set in Sets)
        {
            if (AssetsExist(set))
            {
                list.Add(set);
            }
        }

        return list;
    }

    public static bool TryGetById(int id, out ScreenSaverAssetSet set)
    {
        foreach (var candidate in Sets)
        {
            if (candidate.Id == id)
            {
                set = candidate;
                return true;
            }
        }

        set = Default;
        return false;
    }

    public static string ResolveVideoPath(ScreenSaverAssetSet set) =>
        ScreenSaverMediaCache.ResolveVideoPath(set.VideoFileName);

    public static string ResolveAudioPath(ScreenSaverAssetSet set) =>
        ScreenSaverMediaCache.ResolveAudioPath(set.AudioFileName);

    public static bool AssetsExist(ScreenSaverAssetSet set)
    {
        ScreenSaverMediaCache.EnsureReady();
        return ScreenSaverMediaCache.VideoExists(set.VideoFileName)
            && ScreenSaverMediaCache.AudioExists(set.AudioFileName);
    }
}
