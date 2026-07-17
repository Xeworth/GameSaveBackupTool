namespace GSBT.Core.Common;

/// <summary>Cross-process lease that is safe to hold across async continuations.</summary>
public sealed class OperationFileLease : IAsyncDisposable, IDisposable
{
    private readonly FileStream _stream;

    private OperationFileLease(FileStream stream)
    {
        _stream = stream;
    }

    public static OperationFileLease Acquire(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose);
                return new OperationFileLease(stream);
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(100);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException("Another GSBT process is still using this backup location.", last);
    }

    public static async Task<OperationFileLease> AcquireAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose);
                return new OperationFileLease(stream);
            }
            catch (IOException ex)
            {
                last = ex;
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Another GSBT process is still using this backup location.", last);
    }

    public void Dispose() => _stream.Dispose();

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
