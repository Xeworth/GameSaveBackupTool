namespace GSBT.Core.Services;

/// <summary>Loads bundled <c>7z.dll</c> for SharpSevenZip (call once at app startup).</summary>
public static class SevenZipNativeLibrary
{
    private static bool _initialized;
    private static string? _lastError;

    public static bool IsAvailable => _initialized;

    public static string? LastError => _lastError;

    public static bool TryInitialize(string dllPath)
    {
        _lastError = null;
        if (_initialized)
        {
            return true;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            {
                _lastError = $"7z.dll not found at: {dllPath}";
                return false;
            }

            SharpSevenZip.SharpSevenZipBase.SetLibraryPath(Path.GetFullPath(dllPath));
            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

}
