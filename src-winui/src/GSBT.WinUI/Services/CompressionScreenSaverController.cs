using GSBT.WinUI.ViewModels;
using Microsoft.UI.Dispatching;

namespace GSBT.WinUI.Services;

/// <summary>
/// Watches compress progress and triggers the screen saver when enabled and wait time elapses
/// while progress stays under <see cref="ScreenSaverAssetCatalog.TriggerMaxProgressPercent"/>%.
/// </summary>
internal sealed class CompressionScreenSaverController : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueueTimer _watchTimer;
    private DateTimeOffset? _compressStartedUtc;
    private bool _triggeredThisRun;
    private bool _dismissedByUser;
    private bool _isActive;
    private bool _previewMode;
    private bool _simulationMode;

    public event Action? EnterRequested;
    public event Action? ExitRequested;

    public bool IsActive => _isActive;

    public bool IsPreviewMode => _previewMode;

    public CompressionScreenSaverController(MainViewModel viewModel, SettingsStore settings, DispatcherQueue dispatcher)
    {
        _viewModel = viewModel;
        _settings = settings;
        _watchTimer = dispatcher.CreateTimer();
        _watchTimer.Interval = TimeSpan.FromMilliseconds(250);
        _watchTimer.Tick += (_, _) => OnWatchTick();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public void ForcePreview()
    {
        _previewMode = true;
        _dismissedByUser = false;
        _triggeredThisRun = true;
        if (!_isActive)
        {
            _isActive = true;
            EnterRequested?.Invoke();
        }
    }

    public void StartSlowCompressSimulation(int durationSeconds = 25)
    {
        if (_viewModel.IsBusy || _viewModel.IsScanning)
        {
            return;
        }

        _simulationMode = true;
        _ = _viewModel.RunScreenSaverCompressSimulationAsync(durationSeconds);
    }

    public void NotifyUserExit()
    {
        _dismissedByUser = true;
        _previewMode = false;
        if (_isActive)
        {
            EndActive();
        }
    }

    public void NotifyCompressFinished()
    {
        _previewMode = false;
        _simulationMode = false;
        if (_isActive)
        {
            EndActive();
        }

        ResetRunState();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.FooterCompressShowsCancel))
        {
            if (_viewModel.FooterCompressShowsCancel)
            {
                _compressStartedUtc ??= DateTimeOffset.UtcNow;
                if (!_watchTimer.IsRunning)
                {
                    _watchTimer.Start();
                }
            }
            else if (!_previewMode)
            {
                _watchTimer.Stop();
                NotifyCompressFinished();
            }
        }

        if (e.PropertyName is nameof(MainViewModel.IsBusy)
            && !_viewModel.IsBusy
            && !_previewMode)
        {
            _watchTimer.Stop();
            NotifyCompressFinished();
        }
    }

    private void OnWatchTick()
    {
        if (_previewMode || _isActive || _dismissedByUser || _triggeredThisRun)
        {
            return;
        }

        if (!_viewModel.FooterCompressShowsCancel)
        {
            return;
        }

        if (!_simulationMode && !ScreenSaverSettings.IsEnabled(_settings))
        {
            return;
        }

        _compressStartedUtc ??= DateTimeOffset.UtcNow;
        var elapsed = DateTimeOffset.UtcNow - _compressStartedUtc.Value;
        var triggerSeconds = _simulationMode
            ? ScreenSaverAssetCatalog.SimulationTriggerSeconds
            : ScreenSaverSettings.GetWaitSeconds(_settings);
        if (elapsed.TotalSeconds >= triggerSeconds
            && _viewModel.ScanProgress < ScreenSaverAssetCatalog.TriggerMaxProgressPercent)
        {
            _triggeredThisRun = true;
            _isActive = true;
            EnterRequested?.Invoke();
        }
    }

    private void EndActive()
    {
        _isActive = false;
        ExitRequested?.Invoke();
    }

    private void ResetRunState()
    {
        _compressStartedUtc = null;
        _triggeredThisRun = false;
        _dismissedByUser = false;
        _previewMode = false;
        _simulationMode = false;
    }

    public void Dispose()
    {
        _watchTimer.Stop();
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
