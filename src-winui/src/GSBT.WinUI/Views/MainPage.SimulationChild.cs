using System.IO.Pipes;
using System.Text;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;

namespace GSBT.WinUI.Views;

public sealed partial class MainPage
{
    private void StartSimulationIpcListenerIfChild()
    {
        if (!App.IsSandboxSimulationChild)
        {
            return;
        }

        var pipe = SimulationSessionContext.IpcPipeName;
        if (string.IsNullOrWhiteSpace(pipe))
        {
            return;
        }

        _ = Task.Run(() => ListenSimulationPipeLoopAsync(pipe));
    }

    private async Task ListenSimulationPipeLoopAsync(string pipeName)
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync().ConfigureAwait(false);
                using var sr = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var line = await sr.ReadLineAsync().ConfigureAwait(false);
                var cmd = (line ?? string.Empty).Trim();
                var captured = cmd;
                _ = DispatcherQueue.TryEnqueue(() => HandleSimulationPipeCommand(captured));
            }
            catch
            {
                await Task.Delay(400).ConfigureAwait(false);
            }
        }
    }

    internal void HandleSimulationPipeCommand(string cmd)
    {
        if (!App.IsSandboxSimulationChild)
        {
            return;
        }

        if (string.Equals(cmd, SimulationIpc.PreviewCheckpointDrift, StringComparison.Ordinal)
            || string.Equals(cmd, "PREVIEW_LARGE", StringComparison.Ordinal))
        {
            ViewModel.SandboxPreviewYellowLastBackupWarning();
            return;
        }

        if (string.Equals(cmd, SimulationIpc.PreviewBackupIntegrity, StringComparison.Ordinal)
            || string.Equals(cmd, "PREVIEW_SUSPICIOUS", StringComparison.Ordinal))
        {
            ViewModel.SandboxPreviewRedLastBackupWarning();
        }
    }
}
