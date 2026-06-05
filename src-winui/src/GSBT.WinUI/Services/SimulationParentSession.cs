namespace GSBT.WinUI.Services;

/// <summary>Parent sandbox process: remembers the IPC pipe for the last launched simulated child.</summary>
public static class SimulationParentSession
{
    public static string? ActiveChildPipeName { get; set; }

    public static int? ActiveChildProcessId { get; set; }

    public static void Clear()
    {
        ActiveChildPipeName = null;
        ActiveChildProcessId = null;
    }
}
