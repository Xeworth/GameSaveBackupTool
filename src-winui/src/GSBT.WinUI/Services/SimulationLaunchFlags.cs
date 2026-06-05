namespace GSBT.WinUI.Services;

/// <summary>Serialized to <c>flags.json</c> in the simulation session directory when spawning the child process.</summary>
public sealed record SimulationLaunchFlags(
    bool SimulateNoBackupDestination,
    bool SimulateFirstAppLaunch,
    SandboxSevenZipUiMode SevenZipUiOverride,
    bool IncludeSimulatedLargeGameB,
    bool IncludeSimulatedLargeGameC,
    string IpcPipeName);
