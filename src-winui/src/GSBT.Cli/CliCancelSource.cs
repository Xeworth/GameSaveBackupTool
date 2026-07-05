namespace GSBT.Cli;

/// <summary>Links InvokeAsync cancellation with Ctrl+C for graceful backup/compress cancel.</summary>
public sealed class CliCancelSource : IDisposable
{
    private readonly CancellationTokenSource _linked;
    private bool _disposed;

    public CliCancelSource(CancellationToken external)
    {
        _linked = CancellationTokenSource.CreateLinkedTokenSource(external);
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public CancellationToken Token => _linked.Token;

    public bool IsCancellationRequested => _linked.IsCancellationRequested;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Console.CancelKeyPress -= OnCancelKeyPress;
        _linked.Dispose();
        _disposed = true;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        if (!_linked.IsCancellationRequested)
        {
            _linked.Cancel();
        }
    }
}
