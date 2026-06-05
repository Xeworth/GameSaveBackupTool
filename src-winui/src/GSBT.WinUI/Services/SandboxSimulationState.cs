namespace GSBT.WinUI.Services;

public enum SandboxSevenZipUiMode
{
    /// <summary>Detect real <c>7z.exe</c> on disk.</summary>
    Auto,
    /// <summary>Settings shows 7-Zip as installed (compression still uses real detection).</summary>
    SimulatePresent,
    /// <summary>Settings shows 7-Zip as not installed (Get 7-Zip follows the real install flow).</summary>
    SimulateAbsent,
}

/// <summary>
/// Session overrides for the simulated main-app child process (and monitor UI that configures the next launch).
/// </summary>
public sealed class SandboxSimulationState : ISandboxRuntimeOverrides
{
    private bool _simulateNoBackupDestination;
    private bool _simulateFirstAppLaunch;
    private SandboxSevenZipUiMode _sevenZipUiOverride;
    private bool _includeSimulatedLargeGameB;
    private bool _includeSimulatedLargeGameC;

    public event EventHandler? Changed;

    /// <summary>Pretend no default/last backup folder is configured (prompt on backup).</summary>
    public bool SimulateNoBackupDestination
    {
        get => _simulateNoBackupDestination;
        set
        {
            if (_simulateNoBackupDestination == value)
            {
                return;
            }

            _simulateNoBackupDestination = value;
            RaiseChanged();
        }
    }

    /// <summary>Pretend first launch for teaching-tip keys (onboarding, bulk backup, compress) without deleting settings files.</summary>
    public bool SimulateFirstAppLaunch
    {
        get => _simulateFirstAppLaunch;
        set
        {
            if (_simulateFirstAppLaunch == value)
            {
                return;
            }

            _simulateFirstAppLaunch = value;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Sandbox-only: how Settings → Compression reports 7-Zip installed vs not (Python <c>seven_zip_ui_override</c>).
    /// Does not change real <c>7z.exe</c> resolution for actual compress — use for UI testing with <c>-sandbox</c>.
    /// </summary>
    public SandboxSevenZipUiMode SevenZipUiOverride
    {
        get => _sevenZipUiOverride;
        set
        {
            if (_sevenZipUiOverride == value)
            {
                return;
            }

            _sevenZipUiOverride = value;
            RaiseChanged();
        }
    }

    /// <summary>Dummy catalog: include Game B (Large tier, ≥4 GiB simulated size in estimates).</summary>
    public bool IncludeSimulatedLargeGameB
    {
        get => _includeSimulatedLargeGameB;
        set
        {
            if (_includeSimulatedLargeGameB == value)
            {
                return;
            }

            _includeSimulatedLargeGameB = value;
            RaiseChanged();
        }
    }

    /// <summary>Dummy catalog: include Game C (Suspicious tier, ≥8 GiB simulated size in estimates).</summary>
    public bool IncludeSimulatedLargeGameC
    {
        get => _includeSimulatedLargeGameC;
        set
        {
            if (_includeSimulatedLargeGameC == value)
            {
                return;
            }

            _includeSimulatedLargeGameC = value;
            RaiseChanged();
        }
    }

    /// <summary>Apply values written by the parent process before launching the simulated child.</summary>
    public void ApplyLaunchSnapshot(SimulationLaunchFlags flags)
    {
        _simulateNoBackupDestination = flags.SimulateNoBackupDestination;
        _simulateFirstAppLaunch = flags.SimulateFirstAppLaunch;
        _sevenZipUiOverride = flags.SevenZipUiOverride;
        _includeSimulatedLargeGameB = flags.IncludeSimulatedLargeGameB;
        _includeSimulatedLargeGameC = flags.IncludeSimulatedLargeGameC;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
