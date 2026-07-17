using System.Security.Cryptography;
using System.Text;

namespace GSBT.Core.Common;

/// <summary>Named mutex wrapper for short, cross-process GSBT critical sections.</summary>
public sealed class CrossProcessLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private CrossProcessLock(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static CrossProcessLock Acquire(string scope, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A lock scope is required.", nameof(scope));
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Trim().ToUpperInvariant())));
        var mutex = new Mutex(false, $"Local\\GSBT-{digest[..32]}");
        var owns = false;
        try
        {
            try
            {
                owns = mutex.WaitOne(timeout ?? TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                owns = true;
            }

            if (!owns)
            {
                throw new TimeoutException("Another GSBT process is still using this resource.");
            }

            return new CrossProcessLock(mutex, true);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
