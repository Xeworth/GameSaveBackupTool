namespace GSBT.WinUI.Services;

/// <summary>Production / sandbox parent main window: simulation hooks are inert.</summary>
public sealed class SandboxRuntimeOverridesNone : ISandboxRuntimeOverrides
{
    public static readonly SandboxRuntimeOverridesNone Instance = new();

    private SandboxRuntimeOverridesNone()
    {
    }

    public bool SimulateNoBackupDestination => false;

    public bool SimulateFirstAppLaunch => false;

    public SandboxSevenZipUiMode SevenZipUiOverride => SandboxSevenZipUiMode.Auto;

    public bool IncludeSimulatedLargeGameB => false;

    public bool IncludeSimulatedLargeGameC => false;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
