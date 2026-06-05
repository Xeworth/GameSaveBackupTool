using GSBT.Core.Services;
using GSBT.WinUI.Common;
using GSBT.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml.Navigation;
using WinUIEx;

namespace GSBT.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static Window? MainWindowRef { get; private set; }
        public static IHost? Host { get; private set; }

        /// <summary>Open sandbox monitor on startup (mirrors Python <c>--sandbox</c> / <c>GSBT_SANDBOX</c>).</summary>
        public static bool LaunchSandboxMonitor { get; private set; }

        /// <summary>True while the app is shutting down; suppresses late unhandled-exception dialogs.</summary>
        public static bool IsApplicationExiting { get; private set; }

        public static void MarkApplicationExiting() => IsApplicationExiting = true;

        /// <summary>Second process: simulated main window with dummy catalog (see <see cref="SimulationLaunchParser"/>).</summary>
        public static bool IsSandboxSimulationChild { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                InitializeApp();
            }
            catch (Exception ex)
            {
                StartupFailureReporter.Report(ex, "App.ctor");
                StartupFailureReporter.ShowErrorDialog(ex, "App.ctor");
                throw;
            }
        }

        private void InitializeApp()
        {
            IsSandboxSimulationChild = SimulationLaunchParser.IsSimulationChildProcess();
            if (IsSandboxSimulationChild)
            {
                var sd = SimulationLaunchParser.TryGetSimulationSessionDirectory();
                if (!string.IsNullOrWhiteSpace(sd))
                {
                    SimulationLaunchFlags flags;
                    try
                    {
                        flags = SimulationFlagsSerializer.Read(Path.Combine(sd, "flags.json"));
                    }
                    catch
                    {
                        flags = new SimulationLaunchFlags(
                            false,
                            false,
                            SandboxSevenZipUiMode.Auto,
                            false,
                            false,
                            string.Empty);
                    }

                    SimulationSessionContext.Initialize(sd, flags.IpcPipeName);
                }
            }

            // Application.RequestedTheme must be set before InitializeComponent; setting it in OnLaunched throws COMException (0x80131515).
            try
            {
                if (IsSandboxSimulationChild && SimulationSessionContext.SessionDirectory is { } simDir)
                {
                    var store = new SettingsStore(simDir);
                    RequestedTheme = ThemeBridge.ResolveApplicationTheme(store.Get("ui_theme", "dark"));
                }
                else
                {
                    var store = new SettingsStore();
                    RequestedTheme = ThemeBridge.ResolveApplicationTheme(store.Get("ui_theme", "dark"));
                }
            }
            catch
            {
                // Keep App.xaml RequestedTheme when settings are unreadable.
            }

            InitializeComponent();
            UnhandledException += (_, e) =>
            {
                if (IsApplicationExiting)
                {
                    e.Handled = true;
                    return;
                }

                TryWriteCrashLog(e.Exception);
                StartupFailureReporter.ShowErrorDialog(e.Exception, "UnhandledException");
                e.Handled = true;
            };

            Host = BuildHost();

            LaunchSandboxMonitor = !IsSandboxSimulationChild && SandboxLaunchParser.ShouldOpenMonitor();
        }

        private static IHost BuildHost()
        {
            if (IsSandboxSimulationChild && SimulationSessionContext.SessionDirectory is { } sessionDir)
            {
                return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                    .UseContentRoot(AppContext.BaseDirectory)
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(_ => new SettingsStore(sessionDir));
                        services.AddSingleton<SandboxLogHub>(sp => new SandboxLogHub(sp.GetRequiredService<SettingsStore>()));
                        services.AddSingleton<SandboxMonitorSession>();
                        services.AddSingleton<SandboxSimulationState>(_ =>
                        {
                            var st = new SandboxSimulationState();
                            try
                            {
                                var flags = SimulationFlagsSerializer.Read(Path.Combine(sessionDir, "flags.json"));
                                st.ApplyLaunchSnapshot(flags);
                            }
                            catch
                            {
                                // leave defaults
                            }

                            return st;
                        });
                        services.AddSingleton<ISandboxRuntimeOverrides>(sp => sp.GetRequiredService<SandboxSimulationState>());
                        services.AddSingleton<CompressionActivityTracker>();
                        services.AddSingleton<IGameDetector, WindowsGameDetector>();
                        services.AddSingleton<WinUiTrayService>();
                        services.AddSingleton<MainViewModel>();
                    })
                    .Build();
            }

            return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<SettingsStore>();
                    services.AddSingleton<SandboxLogHub>(sp => new SandboxLogHub(sp.GetRequiredService<SettingsStore>()));
                    services.AddSingleton<SandboxMonitorSession>();
                    services.AddSingleton<SandboxSimulationState>();
                    services.AddSingleton<ISandboxRuntimeOverrides>(_ => SandboxRuntimeOverridesNone.Instance);
                    services.AddSingleton<SandboxCompressionBenchmarkStore>();
                    services.AddSingleton<CompressionActivityTracker>();
                    services.AddSingleton<SandboxResourceMonitor>();
                    services.AddSingleton<SandboxBatchPerformanceHub>();
                    services.AddSingleton<IGameDetector, WindowsGameDetector>();
                    services.AddSingleton<WinUiTrayService>();
                    services.AddSingleton<MainViewModel>();
                })
                .Build();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                LaunchMainWindow(e);
            }
            catch (Exception ex)
            {
                StartupFailureReporter.Report(ex, "OnLaunched");
                StartupFailureReporter.ShowErrorDialog(ex, "OnLaunched");
                throw;
            }
        }

        private void LaunchMainWindow(LaunchActivatedEventArgs e)
        {
            StartupLaunchPresentation.ParseCommandLine(Environment.GetCommandLineArgs());

            _window ??= new Window();
            MainWindowRef = _window;
            _window.ExtendsContentIntoTitleBar = false;
            _window.Title = IsSandboxSimulationChild
                ? $"{AppAboutInfo.AppName} (Simulation)"
                : AppAboutInfo.AppName;

            if (_window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                _window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            AppPaths.MigrateLegacyCrashLogIfNeeded();
            _window.Activate();
            var settingsStore = Host!.Services.GetRequiredService<SettingsStore>();
            WindowSizeHelper.ApplyMainWindowFromSettings(settingsStore, _window);
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => StartupLaunchPresentation.ApplyIfNeeded(_window));
            WindowSizeHelper.ApplyMinimumClientSize(_window);
            TitleBarThemeHelper.ApplyApplicationTheme(_window, Application.Current.RequestedTheme);
            AppBrandingIcons.TryApplySessionIcon(_window, LaunchSandboxMonitor && !IsSandboxSimulationChild);
            OsAppNotifications.EnsureRegistered();
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation that failed</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>Writes the same crash text to temp (easy paste) and %AppData%\Roaming\GSBT\logs (persistent).</summary>
        internal static void TryWriteCrashLog(Exception? ex)
        {
            if (ex is null)
            {
                return;
            }

            var body =
                $"{DateTime.UtcNow:O} UTC\n\n" +
                ex.ToString();

            try
            {
                var temp = Path.Combine(Path.GetTempPath(), "gsbt_winui_last_error.txt");
                File.WriteAllText(temp, body);
            }
            catch
            {
                // ignore
            }

            try
            {
                AppPaths.MigrateLegacyCrashLogIfNeeded();
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                File.WriteAllText(AppPaths.WinUiCrashLogPath, body);
            }
            catch
            {
                // ignore
            }
        }
    }

    internal static class SandboxLaunchParser
    {
        public static bool ShouldOpenMonitor()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("GSBT_SANDBOX"), "1", StringComparison.Ordinal))
            {
                return true;
            }

            if (IsSandboxExecutableName())
            {
                return true;
            }

            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
            {
                if (string.Equals(arg, "-s", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arg, "--sandbox", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary><c>gsbt-sandbox.exe</c> is a hard link to <c>gsbt.exe</c> created by the installer (same as <c>-s</c>).</summary>
        private static bool IsSandboxExecutableName()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return false;
            }

            return string.Equals(Path.GetFileName(exe), "gsbt-sandbox.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
