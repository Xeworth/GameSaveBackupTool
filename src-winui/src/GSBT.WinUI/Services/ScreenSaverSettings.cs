namespace GSBT.WinUI.Services;

internal static class ScreenSaverSettings
{
    public const string EnabledKey = "compression_screen_saver_enabled";
    public const string WaitSecondsKey = "compression_screen_saver_wait_seconds";

    public static bool IsEnabled(SettingsStore store) => store.Get(EnabledKey, true);

    public static int GetWaitSeconds(SettingsStore store) => NormalizeWaitSeconds(store.Get(WaitSecondsKey, 60));

    public static int NormalizeWaitSeconds(int seconds)
    {
        var stepped = ((seconds + 5) / 10) * 10;
        return Math.Clamp(stepped, 10, 60);
    }
}
