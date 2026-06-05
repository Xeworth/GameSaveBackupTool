using System.Linq;

namespace GSBT.WinUI.Services;
/// <summary>Parses <c>--sandbox-sim-child</c> and <c>--sim-session-dir=...</c> from the command line.</summary>
public static class SimulationLaunchParser
{
    public const string SimChildArg = "--sandbox-sim-child";
    public const string SessionDirPrefix = "--sim-session-dir=";

    public static string? TryGetSimulationSessionDirectory()
    {
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg.StartsWith(SessionDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var v = arg[SessionDirPrefix.Length..].Trim().Trim('"');
                return string.IsNullOrWhiteSpace(v) ? null : Path.GetFullPath(v);
            }
        }

        return null;
    }

    public static bool IsSimulationChildProcess() =>
        Environment.GetCommandLineArgs().Skip(1).Any(a => string.Equals(a, SimChildArg, StringComparison.OrdinalIgnoreCase))
        && TryGetSimulationSessionDirectory() is not null;
}
