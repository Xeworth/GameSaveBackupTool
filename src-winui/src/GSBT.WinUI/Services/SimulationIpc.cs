using System.IO.Pipes;
using System.Text;

namespace GSBT.WinUI.Services;

internal static class SimulationIpc
{
    /// <summary>Child should run the same yellow Last backup + toast path as live snapshot drift (sandbox monitor button).</summary>
    public const string PreviewCheckpointDrift = "PREVIEW_CHECKPOINT_DRIFT";

    /// <summary>Child should run the same red Last backup + toast path as missing retention backups under the default path (sandbox monitor button).</summary>
    public const string PreviewBackupIntegrity = "PREVIEW_BACKUP_INTEGRITY";

    public static bool TrySendToChild(string? pipeName, string command)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1500);
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
