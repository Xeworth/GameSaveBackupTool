using System.Reflection;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIEx;

namespace GSBT.WinUI.Services;

public sealed class WinUiTrayService : IDisposable
{

    private WindowManager? _windowManager;
    private Window? _window;
    private MainViewModel? _viewModel;
    private MenuFlyoutItem? _trayProgressItem;
    private MenuFlyoutItem? _trayCancelItem;
    private bool _trayDoubleClickAttached;

    /// <summary>Tray integration is active (closing can hide to tray instead of exiting).</summary>
    public bool IsTrayAvailable => _windowManager is not null;

    /// <summary>Tray menu: Show, optional progress + Cancel while busy, Backup, Compress, Quit. Double-click tray icon restores the window.</summary>
    /// <param name="appendSimulatedFooter">When true, adds a disabled “(simulated)” line after Quit so the sandbox child is not confused with the real app.</param>
    public void Initialize(
        Window window,
        DispatcherQueue dispatcher,
        MainViewModel viewModel,
        Func<Task> onShow,
        Func<Task> onBackup,
        Func<Task> onCompress,
        Func<Task> onQuit,
        bool appendSimulatedFooter = false)
    {
        if (_windowManager is not null)
        {
            return;
        }

        _window = window;
        _viewModel = viewModel;
        _windowManager = WindowManager.Get(window);
        _windowManager.IsVisibleInTray = true;

        TryAttachTrayDoubleClick(onShow, dispatcher);
        dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => TryAttachTrayDoubleClick(onShow, dispatcher));

        _windowManager.TrayIconContextMenu += (_, args) =>
        {
            var flyout = new MenuFlyout();
            GsbtMenuFlyoutChrome.ApplyToFlyout(flyout);
            _trayProgressItem = null;
            _trayCancelItem = null;

            var open = CreateTrayItem("Show");
            open.Click += (_, _) => dispatcher.TryEnqueue(async () => await onShow().ConfigureAwait(true));

            var backup = CreateTrayItem("Backup");
            backup.Click += (_, _) => dispatcher.TryEnqueue(async () => await onBackup().ConfigureAwait(true));
            var compress = CreateTrayItem("Compress");
            compress.Click += (_, _) => dispatcher.TryEnqueue(async () => await onCompress().ConfigureAwait(true));
            var quit = CreateTrayItem("Quit");
            quit.Click += (_, _) => dispatcher.TryEnqueue(async () => await onQuit().ConfigureAwait(true));

            flyout.Items.Add(open);
            flyout.Items.Add(backup);
            flyout.Items.Add(compress);
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(quit);
            if (appendSimulatedFooter)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                flyout.Items.Add(CreateTrayItem("(simulated)", enabled: false));
            }

            void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainViewModel.IsBusy)
                    or nameof(MainViewModel.ScanProgress)
                    or nameof(MainViewModel.FooterBackupShowsCancel)
                    or nameof(MainViewModel.FooterCompressShowsCancel)
                    or nameof(MainViewModel.CanCancelOperation))
                {
                    dispatcher.TryEnqueue(() => SyncTrayProgressItems(flyout));
                }
            }

            void OnOpened(object? s, object e)
            {
                SyncTrayProgressItems(flyout);
                if (_viewModel is not null)
                {
                    _viewModel.PropertyChanged += OnVmChanged;
                }
            }

            void OnClosed(object? s, object e)
            {
                if (_viewModel is not null)
                {
                    _viewModel.PropertyChanged -= OnVmChanged;
                }

                RemoveTrayProgressItems(flyout);
            }

            flyout.Opened += OnOpened;
            flyout.Closed += OnClosed;

            args.Flyout = flyout;
        };
    }

    private static MenuFlyoutItem CreateTrayItem(string text, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        GsbtMenuFlyoutChrome.ApplyToItem(item);
        return item;
    }

    private void SyncTrayProgressItems(MenuFlyout flyout)
    {
        if (_viewModel is null)
        {
            RemoveTrayProgressItems(flyout);
            return;
        }

        var busy = _viewModel.IsBusy
            && (_viewModel.FooterBackupShowsCancel || _viewModel.FooterCompressShowsCancel);
        if (!busy)
        {
            RemoveTrayProgressItems(flyout);
            return;
        }

        if (_trayProgressItem is null)
        {
            _trayProgressItem = CreateTrayItem(string.Empty, enabled: false);
            _trayCancelItem = CreateTrayItem("Cancel");
            _trayCancelItem.Foreground = new SolidColorBrush(Color.FromArgb(255, 0xc4, 0x2b, 0x1c));
            _trayCancelItem.Click += (_, _) => _viewModel?.CancelOperation();
            flyout.Items.Insert(1, _trayProgressItem);
            flyout.Items.Insert(2, _trayCancelItem);
        }

        var pct = (int)Math.Round(Math.Clamp(_viewModel.ScanProgress, 0, 100));
        var verb = _viewModel.FooterCompressShowsCancel ? "Compressing" : "Backing up";
        _trayProgressItem.Text = $"{verb}… {pct}%";
        try
        {
            _trayProgressItem.Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush
                ?? new SolidColorBrush(Color.FromArgb(255, 0x6b, 0x69, 0xf8));
        }
        catch
        {
            _trayProgressItem.Foreground = new SolidColorBrush(Color.FromArgb(255, 0x6b, 0x69, 0xf8));
        }

        var canCancel = _viewModel.CanCancelOperation;
        _trayCancelItem!.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        _trayCancelItem.IsEnabled = canCancel;
    }

    private void RemoveTrayProgressItems(MenuFlyout flyout)
    {
        if (_trayCancelItem is not null && flyout.Items.Contains(_trayCancelItem))
        {
            flyout.Items.Remove(_trayCancelItem);
        }

        if (_trayProgressItem is not null && flyout.Items.Contains(_trayProgressItem))
        {
            flyout.Items.Remove(_trayProgressItem);
        }

        _trayProgressItem = null;
        _trayCancelItem = null;
    }

    private void TryAttachTrayDoubleClick(Func<Task> onShow, DispatcherQueue dispatcher)
    {
        if (_trayDoubleClickAttached || _windowManager is null)
        {
            return;
        }

        try
        {
            var trayField = _windowManager.GetType().GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic);
            if (trayField?.GetValue(_windowManager) is not TrayIcon trayIcon)
            {
                return;
            }

            trayIcon.LeftDoubleClick += (_, _) =>
            {
                dispatcher.TryEnqueue(async () => await onShow().ConfigureAwait(true));
            };
            _trayDoubleClickAttached = true;
        }
        catch
        {
            // WinUIEx internals may change; double-click is optional enhancement.
        }
    }

    public void Dispose()
    {
        _windowManager = null;
        _window = null;
        _viewModel = null;
        _trayProgressItem = null;
        _trayCancelItem = null;
    }
}
