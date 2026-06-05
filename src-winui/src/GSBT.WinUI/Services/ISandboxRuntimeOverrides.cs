namespace GSBT.WinUI.Services;

/// <summary>
/// Session-only simulation hooks read by <see cref="ViewModels.MainViewModel"/> (backup path, teaching tips, 7-Zip hint, optional large dummy games).
/// The sandbox <em>parent</em> window uses <see cref="SandboxRuntimeOverridesNone"/>; the simulated child process uses <see cref="SandboxSimulationState"/>.
/// </summary>
public interface ISandboxRuntimeOverrides
{
    bool SimulateNoBackupDestination { get; }

    bool SimulateFirstAppLaunch { get; }

    SandboxSevenZipUiMode SevenZipUiOverride { get; }

    /// <summary>When true, dummy scan includes Game B (Large tier in estimates).</summary>
    bool IncludeSimulatedLargeGameB { get; }

    /// <summary>When true, dummy scan includes Game C (Suspicious tier in estimates).</summary>
    bool IncludeSimulatedLargeGameC { get; }

    event EventHandler? Changed;
}
