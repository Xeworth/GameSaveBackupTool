using GSBT.WinUI;
using GSBT.WinUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxStatesView : UserControl
{
    private readonly SandboxSimulationState _state;
    private bool _suppress;

    public SandboxStatesView(SandboxSimulationState state)
    {
        _state = state;
        InitializeComponent();
        Loaded += SandboxStatesView_Loaded;
    }

    private void SandboxStatesView_Loaded(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        try
        {
            NoBackupPathCheck.IsChecked = _state.SimulateNoBackupDestination;
            FirstLaunchTipsCheck.IsChecked = _state.SimulateFirstAppLaunch;
            IncludeGameBCheck.IsChecked = _state.IncludeSimulatedLargeGameB;
            IncludeGameCCheck.IsChecked = _state.IncludeSimulatedLargeGameC;
            SevenZipUiModeCombo.SelectedIndex = _state.SevenZipUiOverride switch
            {
                SandboxSevenZipUiMode.SimulatePresent => 1,
                SandboxSevenZipUiMode.SimulateAbsent => 2,
                _ => 0
            };
            ScreenSaverAssetCombo.SelectedIndex = FindScreenSaverComboIndex(_state.ScreenSaverPreviewAssetId);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void NoBackupPathCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _state.SimulateNoBackupDestination = NoBackupPathCheck.IsChecked == true;
    }

    private void FirstLaunchTipsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _state.SimulateFirstAppLaunch = FirstLaunchTipsCheck.IsChecked == true;
    }

    private void IncludeGameBCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _state.IncludeSimulatedLargeGameB = IncludeGameBCheck.IsChecked == true;
    }

    private void IncludeGameCCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        _state.IncludeSimulatedLargeGameC = IncludeGameCCheck.IsChecked == true;
    }

    private void SevenZipUiModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || SevenZipUiModeCombo.SelectedIndex < 0)
        {
            return;
        }

        _state.SevenZipUiOverride = SevenZipUiModeCombo.SelectedIndex switch
        {
            1 => SandboxSevenZipUiMode.SimulatePresent,
            2 => SandboxSevenZipUiMode.SimulateAbsent,
            _ => SandboxSevenZipUiMode.Auto
        };
    }

    private void ScreenSaverAssetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ScreenSaverAssetCombo.SelectedIndex < 0)
        {
            return;
        }

        _state.ScreenSaverPreviewAssetId = ParseScreenSaverComboTag();
    }

    private int FindScreenSaverComboIndex(int assetId)
    {
        for (var i = 0; i < ScreenSaverAssetCombo.Items.Count; i++)
        {
            if (ScreenSaverAssetCombo.Items[i] is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), out var tag)
                && tag == assetId)
            {
                return i;
            }
        }

        return 0;
    }

    private int ParseScreenSaverComboTag()
    {
        if (ScreenSaverAssetCombo.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out var tag))
        {
            return tag;
        }

        return 0;
    }

    private void SimulateMainAppButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var store = App.Host!.Services.GetRequiredService<SettingsStore>();
            var p = SimulationChildLauncher.TryLaunchFromMonitor(_state, store);
            if (p is null)
            {
                _ = ShowSimpleDialogAsync("Could not start", "Failed to start the simulated main window (see logs).");
            }
        }
        catch (Exception ex)
        {
            _ = ShowSimpleDialogAsync("Error", ex.Message);
        }
    }

    private async void PreviewYellowButton_Click(object sender, RoutedEventArgs e)
    {
        await SendPreviewAsync(SimulationIpc.PreviewCheckpointDrift).ConfigureAwait(true);
    }

    private async void PreviewRedButton_Click(object sender, RoutedEventArgs e)
    {
        await SendPreviewAsync(SimulationIpc.PreviewBackupIntegrity).ConfigureAwait(true);
    }

    private async Task SendPreviewAsync(string command)
    {
        if (!SimulationIpc.TrySendToChild(SimulationParentSession.ActiveChildPipeName, command))
        {
            await ShowSimpleDialogAsync(
                "Preview",
                "Could not reach the simulated window. Open the simulated main window first, then try again.")
                .ConfigureAwait(true);
        }
    }

    private async Task ShowSimpleDialogAsync(string title, string message)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dlg = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords, MaxWidth = 420 },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        await GsbtContentDialog.ShowAsync(dlg);
    }

    private void PreviewScreenSaverButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMainPage(out var mainPage))
        {
            _ = ShowSimpleDialogAsync(
                "Screen saver",
                "Could not reach the main GSBT window. Show the main app first, then try again.");
            return;
        }

        mainPage.PreviewCompressionScreenSaver();
    }

    private void SimulateSlowCompressButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetMainPage(out var mainPage))
        {
            _ = ShowSimpleDialogAsync(
                "Screen saver",
                "Could not reach the main GSBT window. Show the main app first, then try again.");
            return;
        }

        mainPage.SimulateSlowCompressForScreenSaver(25);
    }

    private static bool TryGetMainPage(out MainPage mainPage)
    {
        mainPage = null!;
        try
        {
            if (App.MainWindowRef?.Content is Frame f && f.Content is MainPage mp)
            {
                mainPage = mp;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }
}
