using GSBT.WinUI.Common;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GSBT.WinUI;

/// <summary>Custom entry point: set install-folder CWD and surface startup crashes (installer launches).</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupFailureReporter.InstallGlobalHandlers();

        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
            {
                Directory.SetCurrentDirectory(baseDir);
            }

            SandboxResourceBootstrap.EnsureSandboxPriAlias();

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                    ?? throw new InvalidOperationException("DispatcherQueue is not available.");
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(dispatcherQueue));
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            StartupFailureReporter.ReportAndExit(ex, "Main");
        }
    }
}
