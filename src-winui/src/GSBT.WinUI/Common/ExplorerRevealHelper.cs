using System.Diagnostics;

namespace GSBT.WinUI.Common;

internal static class ExplorerRevealHelper
{
    public static bool TryRevealFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(filePath.Trim());
            if (!File.Exists(full))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{full}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
