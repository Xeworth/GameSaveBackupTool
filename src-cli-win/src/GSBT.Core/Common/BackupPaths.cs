namespace GSBT.Core.Common;

/// <summary>Default backup folder helpers (WinUI first-run prompt; PyQt parity).</summary>
public static class BackupPaths
{
    public const string SuggestedFolderName = "gsbt-backups";

    public static string SuggestedDefaultBackupPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, SuggestedFolderName);
    }
}
