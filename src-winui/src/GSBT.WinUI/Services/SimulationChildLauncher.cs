using System.Diagnostics;

namespace GSBT.WinUI.Services;

/// <summary>Creates a per-launch session directory and starts the simulated main-app child process.</summary>
public static class SimulationChildLauncher
{
    public static string EnsureSessionRootExists()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GSBT",
            "SandboxSimulation",
            "sessions",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "dummy_data"));
        Directory.CreateDirectory(Path.Combine(root, SimulationSessionContext.SessionBackupFolderName));
        return root;
    }

    /// <summary>Writes default child settings and simulation flags, then starts the main app exe with sandbox sim args.</summary>
    public static Process? TryLaunch(SimulationLaunchFlags flags, string sessionDir, string? uiThemeKey = null)
    {
        try
        {
            var store = new SettingsStore(sessionDir);
            if (!string.IsNullOrWhiteSpace(uiThemeKey))
            {
                store.Set("ui_theme", uiThemeKey);
            }

            store.Set("default_backup_path", Path.Combine(sessionDir, SimulationSessionContext.SessionBackupFolderName));
            store.Set("saved_game_list_established", false);

            var flagsPath = Path.Combine(sessionDir, "flags.json");
            SimulationFlagsSerializer.Write(flagsPath, flags);

            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return null;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(SimulationLaunchParser.SimChildArg);
            psi.ArgumentList.Add(SimulationLaunchParser.SessionDirPrefix + sessionDir);

            var p = Process.Start(psi);
            SimulationParentSession.ActiveChildPipeName = flags.IpcPipeName;
            SimulationParentSession.ActiveChildProcessId = p?.Id;
            return p;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Builds flags from the monitor simulation UI and launches the child.</summary>
    public static Process? TryLaunchFromMonitor(SandboxSimulationState sim, SettingsStore parentStore)
    {
        var sessionDir = EnsureSessionRootExists();
        var pipe = "GSBT_SIM_" + Guid.NewGuid().ToString("N");
        var flags = new SimulationLaunchFlags(
            sim.SimulateNoBackupDestination,
            sim.SimulateFirstAppLaunch,
            sim.SevenZipUiOverride,
            sim.IncludeSimulatedLargeGameB,
            sim.IncludeSimulatedLargeGameC,
            pipe);
        var theme = parentStore.Get("ui_theme", "dark");
        return TryLaunch(flags, sessionDir, theme);
    }
}
