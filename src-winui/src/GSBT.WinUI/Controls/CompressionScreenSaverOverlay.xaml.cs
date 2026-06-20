using GSBT.WinUI.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace GSBT.WinUI.Controls;

public sealed partial class CompressionScreenSaverOverlay : UserControl
{
    private const int AudioFadeInSteps = 30;
    private const int CompressingFlashIntervalSeconds = 15;
    private const int CompressingFlashPulseCount = 3;
    private const int CompressingFlashPulseMs = 160;

    private const string VolumeGlyph = "\uE767";
    private const string MuteGlyphValue = "\uE74F";

    private readonly MediaPlayer _audioPlayer = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _audioFadeTimer;
    private readonly DispatcherQueueTimer _compressingFlashTimer;
    private ScreenSaverAssetSet _assetSet = ScreenSaverAssetCatalog.Default;
    private string _dateFormatKey = "iso";
    private bool _isMuted;
    private bool _isShowing;
    private bool _compressingFlashRunning;
    private int _compressingFlashToken;
    private bool _cancelRequested;
    private bool _fileTrackerEnabled;
    private bool _assetTransitionRunning;
    private bool _assetRotationEnabled;

    public event EventHandler? ExitRequested;
    public event EventHandler? TrackEnded;

    /// <summary>When true, video end triggers <see cref="TrackEnded"/> instead of replaying the same ID.</summary>
    public bool AssetRotationEnabled
    {
        get => _assetRotationEnabled;
        set => _assetRotationEnabled = value;
    }

    public CompressionScreenSaverOverlay()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _clockTimer = _dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => RefreshClock();

        _audioFadeTimer = _dispatcher.CreateTimer();
        _audioFadeTimer.Interval = TimeSpan.FromMilliseconds(45);
        _audioFadeTimer.Tick += AudioFadeTimer_Tick;

        _compressingFlashTimer = _dispatcher.CreateTimer();
        _compressingFlashTimer.Interval = TimeSpan.FromSeconds(CompressingFlashIntervalSeconds);
        _compressingFlashTimer.Tick += (_, _) => _ = RunCompressingFlashBurstAsync();

        _audioPlayer.MediaOpened += (_, _) => BeginAudioFadeIn();
        _audioPlayer.IsLoopingEnabled = false;
        _audioPlayer.Volume = 0;

        Loaded += (_, _) =>
        {
            var videoPlayer = VideoPlayer.MediaPlayer;
            videoPlayer.Volume = 0;
            videoPlayer.IsLoopingEnabled = false;
            videoPlayer.MediaEnded += (_, _) => _dispatcher.TryEnqueue(OnVideoTrackEnded);
        };
        Unloaded += (_, _) => StopPlayback();
    }

    public void Configure(string dateFormatKey, int assetId = 1)
    {
        _dateFormatKey = dateFormatKey;
        _assetSet = ScreenSaverAssetCatalog.TryGetById(assetId, out var set) && ScreenSaverAssetCatalog.AssetsExist(set)
            ? set
            : ScreenSaverAssetCatalog.Default;
    }

    public void SetFileTrackerEnabled(bool enabled)
    {
        _fileTrackerEnabled = enabled;
        if (!enabled)
        {
            ClearFileTracker();
        }
    }

    public void UpdateFileTracker(string upcoming, string current, string previous)
    {
        if (!_fileTrackerEnabled)
        {
            return;
        }

        ApplyTrackerLine(FileTrackerUpcomingLine, FormatTrackerLabel(upcoming));
        ApplyTrackerLine(FileTrackerCurrentLine, FormatCurrent(current));
        ApplyTrackerLine(FileTrackerPreviousLine, FormatTrackerLabel(previous));
        FileTrackerPanel.Visibility = HasAnyTrackerText()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void EnsureFileTrackerChromeVisible()
    {
        if (!_fileTrackerEnabled || !_isShowing || FileTrackerPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        FileTrackerPanel.Opacity = 1;
    }

    public void ClearFileTracker()
    {
        FileTrackerUpcomingLine.Text = string.Empty;
        FileTrackerCurrentLine.Text = string.Empty;
        FileTrackerPreviousLine.Text = string.Empty;
        FileTrackerUpcomingLine.Visibility = Visibility.Collapsed;
        FileTrackerCurrentLine.Visibility = Visibility.Collapsed;
        FileTrackerPreviousLine.Visibility = Visibility.Collapsed;
        FileTrackerPanel.Visibility = Visibility.Collapsed;
        FileTrackerPanel.Opacity = 0;
    }

    public void SetCancelRequested(bool cancelRequested)
    {
        if (_cancelRequested == cancelRequested)
        {
            return;
        }

        _cancelRequested = cancelRequested;
        if (!_isShowing)
        {
            return;
        }

        if (cancelRequested)
        {
            StopCompressingFlashLoop();
            CompressingStatusLine.Text = "Canceling...";
            CompressingStatusLine.Opacity = 1;
            return;
        }

        CompressingStatusLine.Text = "Compressing...";
        StartCompressingFlashLoop();
    }

    public async Task ShowAsync(bool skipAnimations = false)
    {
        if (_isShowing)
        {
            return;
        }

        if (!ScreenSaverAssetCatalog.AssetsExist(_assetSet))
        {
            return;
        }

        _isShowing = true;
        _isMuted = false;
        _cancelRequested = false;
        CompressingStatusLine.Text = "Compressing...";
        UpdateMuteGlyph();
        IsHitTestVisible = true;
        Visibility = Visibility.Visible;
        Opacity = 1;

        VideoPopTransform.ScaleX = skipAnimations ? 1.0 : 0.94;
        VideoPopTransform.ScaleY = skipAnimations ? 1.0 : 0.94;
        VideoChromeBorder.Opacity = skipAnimations ? 1 : 0;
        VideoPlayer.Opacity = 1;
        ClockPanel.Opacity = skipAnimations ? 1 : 0;
        FileTrackerPanel.Opacity = skipAnimations && _fileTrackerEnabled && FileTrackerPanel.Visibility == Visibility.Visible ? 1 : 0;
        ControlPanel.Opacity = skipAnimations ? 1 : 0;
        CompressingStatusLine.Opacity = skipAnimations ? 1 : 0;
        RootGrid.Opacity = 1;

        LoadCurrentAssetMedia();
        VideoPlayer.MediaPlayer.Play();
        _audioPlayer.Play();

        if (skipAnimations)
        {
            RefreshClock();
            _clockTimer.Start();
            StartCompressingFlashLoop();
            if (_fileTrackerEnabled && FileTrackerPanel.Visibility == Visibility.Visible)
            {
                FileTrackerPanel.Opacity = 1;
            }

            return;
        }

        await Task.WhenAll(
            AnimateDoubleAsync(VideoChromeBorder, "Opacity", 0, 1, 420),
            AnimateScaleAsync(VideoPopTransform, 0.94, 1.0, 520, overshoot: 1.02));

        var chromeFadeTasks = new List<Task>
        {
            AnimateDoubleAsync(ClockPanel, "Opacity", 0, 1, 360),
            AnimateDoubleAsync(ControlPanel, "Opacity", 0, 1, 360),
            AnimateDoubleAsync(CompressingStatusLine, "Opacity", 0, 1, 360),
        };
        if (_fileTrackerEnabled && FileTrackerPanel.Visibility == Visibility.Visible)
        {
            FileTrackerPanel.Opacity = 0;
            chromeFadeTasks.Add(AnimateDoubleAsync(FileTrackerPanel, "Opacity", 0, 1, 360));
        }

        await Task.WhenAll(chromeFadeTasks);

        RefreshClock();
        _clockTimer.Start();
        StartCompressingFlashLoop();
    }

    public async Task TransitionToAssetAsync(int assetId)
    {
        if (!_isShowing || _assetTransitionRunning)
        {
            return;
        }

        if (!ScreenSaverAssetCatalog.TryGetById(assetId, out var nextSet)
            || !ScreenSaverAssetCatalog.AssetsExist(nextSet))
        {
            return;
        }

        _assetTransitionRunning = true;
        try
        {
            _audioFadeTimer.Stop();
            await Task.WhenAll(
                AnimateDoubleAsync(VideoChromeBorder, "Opacity", VideoChromeBorder.Opacity, 0, 480),
                FadeAudioToAsync(0, 480));

            if (!_isShowing)
            {
                return;
            }

            _assetSet = nextSet;
            LoadCurrentAssetMedia();
            VideoPlayer.Opacity = 1;
            VideoPlayer.MediaPlayer.Play();
            _audioPlayer.Play();

            await AnimateDoubleAsync(VideoChromeBorder, "Opacity", 0, 1, 520);
        }
        finally
        {
            _assetTransitionRunning = false;
        }
    }

    public async Task HideAsync()
    {
        if (!_isShowing)
        {
            return;
        }

        StopCompressingFlashLoop();
        _clockTimer.Stop();
        _audioFadeTimer.Stop();
        await FadeAudioToAsync(0, 420);
        StopPlayback();

        await Task.WhenAll(
            AnimateDoubleAsync(ClockPanel, "Opacity", ClockPanel.Opacity, 0, 220),
            AnimateDoubleAsync(FileTrackerPanel, "Opacity", FileTrackerPanel.Opacity, 0, 220),
            AnimateDoubleAsync(ControlPanel, "Opacity", ControlPanel.Opacity, 0, 220),
            AnimateDoubleAsync(CompressingStatusLine, "Opacity", CompressingStatusLine.Opacity, 0, 220),
            AnimateDoubleAsync(VideoChromeBorder, "Opacity", VideoChromeBorder.Opacity, 0, 280));

        ClearFileTracker();
        _fileTrackerEnabled = false;

        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        _isShowing = false;
    }

    private void LoadCurrentAssetMedia()
    {
        ScreenSaverMediaCache.EnsureReady();
        var videoPath = ScreenSaverAssetCatalog.ResolveVideoPath(_assetSet);
        var audioPath = ScreenSaverAssetCatalog.ResolveAudioPath(_assetSet);
        VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(videoPath));
        _audioPlayer.Source = MediaSource.CreateFromUri(new Uri(audioPath));
    }

    private void OnVideoTrackEnded()
    {
        if (!_isShowing || _assetTransitionRunning)
        {
            return;
        }

        if (_assetRotationEnabled)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        RestartCurrentAsset();
    }

    private void RestartCurrentAsset()
    {
        _audioFadeTimer.Stop();
        VideoPlayer.MediaPlayer.Position = TimeSpan.Zero;
        _audioPlayer.Position = TimeSpan.Zero;
        VideoPlayer.MediaPlayer.Play();
        _audioPlayer.Play();
    }

    private void RefreshClock()
    {
        var (time, date) = ScreenSaverClockFormatter.FormatNow(_dateFormatKey);
        ClockTimeLine.Text = time;
        ClockDateLine.Text = date;
    }

    private bool HasAnyTrackerText() =>
        FileTrackerUpcomingLine.Visibility == Visibility.Visible
        || FileTrackerCurrentLine.Visibility == Visibility.Visible
        || FileTrackerPreviousLine.Visibility == Visibility.Visible;

    private static void ApplyTrackerLine(VcrOutlineTextBlock line, string text)
    {
        var visible = !string.IsNullOrEmpty(text);
        line.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        line.Text = text;
    }

    private static string FormatTrackerLabel(string name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    private static string FormatCurrent(string name)
    {
        var label = FormatTrackerLabel(name);
        return string.IsNullOrEmpty(label) ? string.Empty : $"> {label}";
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _isMuted = !_isMuted;
        UpdateMuteGlyph();
        if (_isMuted)
        {
            _audioFadeTimer.Stop();
            _audioPlayer.Volume = 0;
        }
        else
        {
            _audioPlayer.Volume = ScreenSaverAssetCatalog.AudioBaseVolume;
        }
    }

    private void UpdateMuteGlyph() =>
        MuteButton.IconGlyph = _isMuted ? MuteGlyphValue : VolumeGlyph;

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void StartCompressingFlashLoop()
    {
        if (_cancelRequested)
        {
            CompressingStatusLine.Opacity = 1;
            return;
        }

        _compressingFlashToken++;
        CompressingStatusLine.Opacity = 1;
        _compressingFlashTimer.Start();
    }

    private void StopCompressingFlashLoop()
    {
        _compressingFlashToken++;
        _compressingFlashRunning = false;
        _compressingFlashTimer.Stop();
    }

    private async Task RunCompressingFlashBurstAsync()
    {
        if (!_isShowing || _compressingFlashRunning || _cancelRequested)
        {
            return;
        }

        _compressingFlashRunning = true;
        var token = _compressingFlashToken;
        try
        {
            for (var i = 0; i < CompressingFlashPulseCount && _isShowing && token == _compressingFlashToken; i++)
            {
                CompressingStatusLine.Opacity = 0;
                await Task.Delay(CompressingFlashPulseMs);
                if (!_isShowing || token != _compressingFlashToken)
                {
                    return;
                }

                CompressingStatusLine.Opacity = 1;
                await Task.Delay(CompressingFlashPulseMs);
            }
        }
        finally
        {
            if (token == _compressingFlashToken)
            {
                CompressingStatusLine.Opacity = 1;
                _compressingFlashRunning = false;
            }
        }
    }

    private void BeginAudioFadeIn()
    {
        if (_isMuted)
        {
            return;
        }

        _audioPlayer.Volume = 0;
        _audioFadeTarget = ScreenSaverAssetCatalog.AudioBaseVolume;
        _audioFadeStep = _audioFadeTarget / AudioFadeInSteps;
        _audioFadeTimer.Start();
    }

    private double _audioFadeTarget;
    private double _audioFadeStep;

    private void AudioFadeTimer_Tick(object? sender, object e)
    {
        var next = _audioPlayer.Volume + _audioFadeStep;
        if (_audioFadeStep >= 0 && next >= _audioFadeTarget)
        {
            _audioPlayer.Volume = _audioFadeTarget;
            _audioFadeTimer.Stop();
            return;
        }

        if (_audioFadeStep < 0 && next <= _audioFadeTarget)
        {
            _audioPlayer.Volume = _audioFadeTarget;
            _audioFadeTimer.Stop();
            return;
        }

        _audioPlayer.Volume = Math.Clamp(next, 0, 1);
    }

    private Task FadeAudioToAsync(double target, int durationMs)
    {
        var steps = Math.Max(1, durationMs / 45);
        _audioFadeTarget = target;
        _audioFadeStep = (target - _audioPlayer.Volume) / steps;
        _audioFadeTimer.Start();
        return Task.Delay(durationMs);
    }

    private void StopPlayback()
    {
        try
        {
            VideoPlayer.MediaPlayer.Pause();
            VideoPlayer.Source = null;
        }
        catch
        {
            // ignore
        }

        try
        {
            _audioPlayer.Pause();
            _audioPlayer.Source = null;
        }
        catch
        {
            // ignore
        }
    }

    private static Task AnimateDoubleAsync(DependencyObject target, string property, double from, double to, int durationMs)
    {
        var tcs = new TaskCompletionSource<bool>();
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) => tcs.TrySetResult(true);
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        sb.Children.Add(anim);
        sb.Begin();
        _ = Task.Delay(durationMs + 80).ContinueWith(_ => tcs.TrySetResult(true));
        return tcs.Task;
    }

    private static async Task AnimateScaleAsync(ScaleTransform transform, double from, double to, int durationMs, double overshoot = 1.0)
    {
        if (overshoot > 1.0 && overshoot > to)
        {
            var midMs = (int)(durationMs * 0.62);
            await AnimateScaleAxisAsync(transform, from, overshoot, midMs);
            await AnimateScaleAxisAsync(transform, overshoot, to, durationMs - midMs);
            return;
        }

        await AnimateScaleAxisAsync(transform, from, to, durationMs);
    }

    private static Task AnimateScaleAxisAsync(ScaleTransform transform, double from, double to, int durationMs)
    {
        var tcs = new TaskCompletionSource<bool>();
        var animX = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        };
        var animY = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        };
        animX.Completed += (_, _) => tcs.TrySetResult(true);
        var sb = new Storyboard();
        Storyboard.SetTarget(animX, transform);
        Storyboard.SetTargetProperty(animX, "ScaleX");
        Storyboard.SetTarget(animY, transform);
        Storyboard.SetTargetProperty(animY, "ScaleY");
        sb.Children.Add(animX);
        sb.Children.Add(animY);
        sb.Begin();
        _ = Task.Delay(durationMs + 80).ContinueWith(_ => tcs.TrySetResult(true));
        return tcs.Task;
    }
}
