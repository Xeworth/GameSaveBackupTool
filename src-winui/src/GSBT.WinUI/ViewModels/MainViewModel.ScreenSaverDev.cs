namespace GSBT.WinUI.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Dev/sandbox: fake compress progress so the screen saver trigger can be exercised without real 7-Zip work.</summary>
    public async Task RunScreenSaverCompressSimulationAsync(int durationSeconds = 25)
    {
        if (IsBusy || IsScanning)
        {
            return;
        }

        durationSeconds = Math.Clamp(durationSeconds, 12, 120);
        BeginCancellableOperation(FooterCancelSlot.Compress);
        ScanProgress = 0;
        try
        {
            var token = _operationCts!.Token;
            var steps = Math.Max(1, durationSeconds * 2);
            for (var i = 0; i <= steps && !token.IsCancellationRequested; i++)
            {
                var pct = Math.Min(84, i * 84.0 / steps);
                EnqueueUi(() => ScanProgress = pct);
                await Task.Delay(500, token).ConfigureAwait(false);
            }

            if (!token.IsCancellationRequested)
            {
                EnqueueUi(() => ScanProgress = 100);
                await Task.Delay(800, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Simulated compress cancelled.";
        }
        finally
        {
            EnqueueUi(() =>
            {
                ScanProgress = 0;
                IsBusy = false;
                EndCancellableOperation();
            });
        }
    }
}
