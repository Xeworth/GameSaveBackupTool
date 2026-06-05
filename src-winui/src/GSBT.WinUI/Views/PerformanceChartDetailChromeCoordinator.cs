using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GSBT.WinUI.Views;

/// <summary>Keeps an open chart detail window in sync with sandbox shell theme changes.</summary>
internal sealed class PerformanceChartDetailChromeCoordinator : IDisposable
{
    private readonly Window _dialog;
    private readonly Grid _root;
    private readonly List<PerformanceSparkline> _charts;
    private readonly List<BatchTestCardHost> _cardHosts;
    private readonly List<Action<bool>> _extraRefreshers;
    private SandboxMonitorWindow? _monitor;
    private bool _disposed;

    public PerformanceChartDetailChromeCoordinator(
        Window dialog,
        Grid root,
        List<PerformanceSparkline> charts,
        List<BatchTestCardHost> cardHosts,
        List<Action<bool>> extraRefreshers)
    {
        _dialog = dialog;
        _root = root;
        _charts = charts;
        _cardHosts = cardHosts;
        _extraRefreshers = extraRefreshers;
    }

    public void AttachOwner(Window ownerWindow, ElementTheme initialTheme)
    {
        Apply(initialTheme);
        if (ownerWindow is SandboxMonitorWindow monitor)
        {
            _monitor = monitor;
            monitor.ShellChromeThemeChanged += OnShellThemeChanged;
        }

        ThemeBridge.ShellThemeChanged += OnShellThemeChanged;
    }

    private void OnShellThemeChanged(ElementTheme theme) =>
        Apply(theme);

    public void Apply(ElementTheme theme)
    {
        if (_disposed)
        {
            return;
        }

        var dark = theme != ElementTheme.Light;
        _root.RequestedTheme = theme;
        _root.Background = ThemeBridge.GetGsbtBrush(dark, "GsbtWindowBgBrush");
        try
        {
            TitleBarThemeHelper.Apply(_dialog, theme);
        }
        catch
        {
            // ignore
        }

        foreach (var chart in _charts)
        {
            chart.DarkPlotChrome = dark;
            chart.Redraw();
        }

        foreach (var host in _cardHosts)
        {
            BatchTestCardBuilder.ApplyChromeTheme(host, dark);
        }

        foreach (var refresh in _extraRefreshers)
        {
            refresh(dark);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_monitor is not null)
        {
            _monitor.ShellChromeThemeChanged -= OnShellThemeChanged;
        }

        ThemeBridge.ShellThemeChanged -= OnShellThemeChanged;
    }
}

internal sealed class PerformanceChartDetailChromeCollector
{
    private readonly List<PerformanceSparkline> _charts = [];
    private readonly List<BatchTestCardHost> _cardHosts = [];
    private readonly List<Action<bool>> _extraRefreshers = [];

    public void RegisterChart(PerformanceSparkline chart) => _charts.Add(chart);

    public void RegisterCardHosts(IEnumerable<BatchTestCardHost> hosts) => _cardHosts.AddRange(hosts);

    public void RegisterExtraRefresh(Action<bool> refresh) => _extraRefreshers.Add(refresh);

    public PerformanceChartDetailChromeCoordinator Bind(Window dialog, Grid root, Window ownerWindow, ElementTheme initialTheme)
    {
        var coordinator = new PerformanceChartDetailChromeCoordinator(dialog, root, _charts, _cardHosts, _extraRefreshers);
        coordinator.AttachOwner(ownerWindow, initialTheme);
        return coordinator;
    }
}
