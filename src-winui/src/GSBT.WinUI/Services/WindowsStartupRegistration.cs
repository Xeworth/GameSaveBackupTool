using Microsoft.Win32;

namespace GSBT.WinUI.Services;

/// <summary>HKCU Run registration — mirrors Python <c>set_startup</c> (value name <c>GameSaveBackupTool</c>).</summary>
public static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameSaveBackupTool";

    /// <param name="mode"><c>disabled</c>, <c>normal</c>, <c>minimized</c>, or <c>hidden</c>.</param>
    public static void Apply(string mode)
    {
        var m = (mode ?? "disabled").Trim().ToLowerInvariant();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (m == "disabled")
            {
                try
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                catch
                {
                    // ignore
                }

                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return;
            }

            var quoted = '"' + exe + '"';
            var cmd = m switch
            {
                "minimized" => $"{quoted} --minimized",
                "hidden" => $"{quoted} --hidden",
                _ => quoted
            };

            key.SetValue(ValueName, cmd, RegistryValueKind.String);
        }
        catch
        {
            // registry failures are non-fatal
        }
    }
}
