using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using WinRT.Interop;

namespace GSBT.WinUI.Services;

internal sealed record OwnedFolderPickerResult(string? Path, string? Error)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(Path);
}

internal static class OwnedFolderPicker
{
    public static async Task<OwnedFolderPickerResult> PickSingleFolderAsync(Window? owner)
    {
        if (owner is null)
        {
            return Failed("The main window is not ready.");
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(owner);
            if (hwnd == IntPtr.Zero)
            {
                return Failed("The main window handle is unavailable.");
            }

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FolderPicker(windowId)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };

            var folder = await picker.PickSingleFolderAsync();
            return new OwnedFolderPickerResult(folder?.Path, null);
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message.Trim();
            return Failed($"{detail} (HRESULT 0x{ex.HResult:X8})");
        }
    }

    private static OwnedFolderPickerResult Failed(string message) =>
        new(null, message);
}
