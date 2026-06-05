namespace GSBT.WinUI.Services;

/// <summary>Per-process simulation session (set early in <see cref="App"/> for the child window).</summary>
public static class SimulationSessionContext
{
    /// <summary>Recommended backup folder name under each simulation session (on-disk folder name).</summary>
    public const string SessionBackupFolderName = "GSBT_Backup_Sim";

    public static string? SessionDirectory { get; private set; }

    public static string? IpcPipeName { get; private set; }

    /// <summary>Root of bundled dummy save trees (under install <c>data/sandbox_simulation</c>).</summary>
    public static string BundledDummyDataRoot =>
        Path.Combine(AppContext.BaseDirectory, "data", "sandbox_simulation");

    public static void Initialize(string sessionDirectory, string ipcPipeName)
    {
        SessionDirectory = sessionDirectory;
        IpcPipeName = ipcPipeName;
    }

    public static string SessionDummyDataRoot =>
        SessionDirectory is null
            ? throw new InvalidOperationException("Simulation session not initialized.")
            : Path.Combine(SessionDirectory, "dummy_data");

    public static string SessionBackupRoot =>
        SessionDirectory is null
            ? throw new InvalidOperationException("Simulation session not initialized.")
            : Path.Combine(SessionDirectory, SessionBackupFolderName);
}
