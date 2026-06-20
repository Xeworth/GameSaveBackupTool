using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices;
using Windows.UI;

namespace GSBT.WinUI.Services;

/// <summary>Applies themed progress-bar foreground brushes for compression screen saver IDs.</summary>
internal static class ScreenSaverProgressTheme
{
    private static readonly Dictionary<string, LinearGradientBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    private static DispatcherTimer? _shimmerTimer;
    private static LinearGradientBrush? _activeBrush;
    private static double _shimmerPhase;
    private static Panel? _bubbleHost;
    private static ProgressBar? _trackedProgressBar;
    private static Canvas? _bubbleCanvas;
    private static DispatcherTimer? _bubbleTimer;
    private static readonly List<BubbleAnim> _bubbles = [];

    private sealed class BubbleAnim
    {
        public Ellipse Shape = null!;
        public double Phase;
        public double BaseX;
        public double RiseSpeed;
    }

    private sealed record ShimmerPalette(Color EdgeBase, Color EdgeHighlight, Color BubbleFill);

    public static void ApplyTheme(ProgressBar progressBar, Panel bubbleHost, string themeKey, bool animateIn)
    {
        StopShimmer();
        StopBubbles();
        _bubbleHost = bubbleHost;
        _trackedProgressBar = progressBar;

        var brush = CreateBrush(themeKey);
        _activeBrush = brush;
        progressBar.Foreground = brush;
        ApplyTrackBackground(progressBar, themeKey);
        StartShimmer(brush, themeKey);
        StartBubbles(bubbleHost, progressBar, themeKey);

        if (animateIn)
        {
            AnimateThemeIn(progressBar);
        }
    }

    public static Task TransitionThemeAsync(
        ProgressBar progressBar,
        Panel bubbleHost,
        string toThemeKey,
        int durationMs = 520)
    {
        StopShimmer();
        StopBubbles();

        var halfMs = Math.Max(120, durationMs / 2);
        var tcs = new TaskCompletionSource<bool>();
        var fadeOut = new DoubleAnimation
        {
            From = progressBar.Opacity,
            To = 0.2,
            Duration = new Duration(TimeSpan.FromMilliseconds(halfMs)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fadeOut.Completed += (_, _) =>
        {
            ApplyTheme(progressBar, bubbleHost, toThemeKey, animateIn: false);
            var fadeIn = new DoubleAnimation
            {
                From = 0.2,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(halfMs)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            fadeIn.Completed += (_, _) => tcs.TrySetResult(true);
            var sbIn = new Storyboard();
            Storyboard.SetTarget(fadeIn, progressBar);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            sbIn.Children.Add(fadeIn);
            sbIn.Begin();
        };
        var sbOut = new Storyboard();
        Storyboard.SetTarget(fadeOut, progressBar);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        sbOut.Children.Add(fadeOut);
        sbOut.Begin();
        _ = Task.Delay(durationMs + 80).ContinueWith(_ => tcs.TrySetResult(true));
        return tcs.Task;
    }

    public static void RestoreDefault(ProgressBar progressBar, Panel bubbleHost, bool animateOut)
    {
        StopShimmer();
        StopBubbles();
        _activeBrush = null;
        _bubbleHost = null;
        progressBar.ClearValue(ProgressBar.ForegroundProperty);
        progressBar.ClearValue(ProgressBar.BackgroundProperty);

        if (animateOut)
        {
            PulseRestore(progressBar);
        }
    }

    private static void ApplyTrackBackground(ProgressBar progressBar, string themeKey)
    {
        var trackBrush = GetTrackBackgroundBrush(themeKey);
        if (trackBrush is null)
        {
            progressBar.ClearValue(ProgressBar.BackgroundProperty);
            return;
        }

        progressBar.Background = trackBrush;
    }

    private static SolidColorBrush? GetTrackBackgroundBrush(string themeKey) =>
        themeKey.ToLowerInvariant() switch
        {
            "sunset-glow" => new SolidColorBrush(Color.FromArgb(255, 72, 10, 14)),
            "forest-green" => new SolidColorBrush(Color.FromArgb(255, 10, 36, 18)),
            "bloom-pink" => new SolidColorBrush(Color.FromArgb(255, 52, 12, 34)),
            "water-blue" => new SolidColorBrush(Color.FromArgb(255, 8, 28, 58)),
            _ => null,
        };

    private static LinearGradientBrush CreateBrush(string themeKey) =>
        themeKey.ToLowerInvariant() switch
        {
            "sunset-glow" => BuildSunsetGlowBrush(),
            "forest-green" => BuildForestGreenBrush(),
            "bloom-pink" => BuildBloomPinkBrush(),
            "water-blue" => BuildWaterBlueBrush(),
            _ => BuildWaterBlueBrush(),
        };

    private static ShimmerPalette GetShimmerPalette(string themeKey) =>
        themeKey.ToLowerInvariant() switch
        {
            "sunset-glow" => new ShimmerPalette(
                Color.FromArgb(255, 168, 24, 32),
                Color.FromArgb(255, 235, 72, 78),
                Color.FromArgb(120, 255, 120, 128)),
            "forest-green" => new ShimmerPalette(
                Color.FromArgb(255, 18, 92, 48),
                Color.FromArgb(255, 52, 148, 82),
                Color.FromArgb(120, 120, 210, 140)),
            "bloom-pink" => new ShimmerPalette(
                Color.FromArgb(255, 168, 42, 108),
                Color.FromArgb(255, 232, 96, 158),
                Color.FromArgb(120, 255, 160, 200)),
            _ => new ShimmerPalette(
                Color.FromArgb(255, 10, 144, 255),
                Color.FromArgb(255, 40, 170, 255),
                Color.FromArgb(120, 180, 230, 255)),
        };

    private static LinearGradientBrush BuildSunsetGlowBrush()
    {
        if (Brushes.TryGetValue("sunset-glow", out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 168, 24, 32), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 220, 48, 54), Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 168, 24, 32), Offset = 1.0 });
        Brushes["sunset-glow"] = brush;
        return brush;
    }

    private static LinearGradientBrush BuildWaterBlueBrush()
    {
        if (Brushes.TryGetValue("water-blue", out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 10, 144, 255), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 94, 196, 255), Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 10, 144, 255), Offset = 1.0 });
        Brushes["water-blue"] = brush;
        return brush;
    }

    private static LinearGradientBrush BuildForestGreenBrush()
    {
        if (Brushes.TryGetValue("forest-green", out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 18, 92, 48), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 38, 128, 68), Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 18, 92, 48), Offset = 1.0 });
        Brushes["forest-green"] = brush;
        return brush;
    }

    private static LinearGradientBrush BuildBloomPinkBrush()
    {
        if (Brushes.TryGetValue("bloom-pink", out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 168, 42, 108), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 214, 72, 138), Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 168, 42, 108), Offset = 1.0 });
        Brushes["bloom-pink"] = brush;
        return brush;
    }

    private static void StartShimmer(LinearGradientBrush brush, string themeKey)
    {
        var palette = GetShimmerPalette(themeKey);
        _shimmerPhase = 0;
        _shimmerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _shimmerTimer.Tick += (_, _) =>
        {
            if (brush.GradientStops.Count < 3)
            {
                return;
            }

            _shimmerPhase += 0.045;
            var wave = (Math.Sin(_shimmerPhase) + 1.0) * 0.5;
            var highlight = 0.12 + (wave * 0.76);
            brush.GradientStops[1].Offset = highlight;

            var edgeWave = (Math.Sin(_shimmerPhase * 0.7 + 1.2) + 1.0) * 0.5;
            brush.GradientStops[0].Color = Blend(palette.EdgeBase, palette.EdgeHighlight, edgeWave * 0.42);
            brush.GradientStops[2].Color = Blend(palette.EdgeBase, palette.EdgeHighlight, (1 - edgeWave) * 0.42);
            brush.GradientStops[1].Color = Blend(palette.EdgeBase, palette.EdgeHighlight, 0.55 + (wave * 0.35));
        };
        _shimmerTimer.Start();
    }

    private static void StopShimmer()
    {
        StopDispatcherTimer(ref _shimmerTimer);
    }

    private static void StartBubbles(Panel host, ProgressBar progressBar, string themeKey)
    {
        var palette = GetShimmerPalette(themeKey);
        _bubbleCanvas = new Canvas
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        host.Children.Add(_bubbleCanvas);
        Canvas.SetZIndex(_bubbleCanvas, 4);

        _bubbles.Clear();
        var rng = Random.Shared;
        for (var i = 0; i < 5; i++)
        {
            var size = 2.0 + (rng.NextDouble() * 2.5);
            var bubble = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(palette.BubbleFill),
                Opacity = 0.35 + (rng.NextDouble() * 0.35),
            };
            _bubbleCanvas.Children.Add(bubble);
            _bubbles.Add(new BubbleAnim
            {
                Shape = bubble,
                Phase = rng.NextDouble() * Math.PI * 2,
                BaseX = 0.08 + (rng.NextDouble() * 0.84),
                RiseSpeed = 0.035 + (rng.NextDouble() * 0.025),
            });
        }

        host.SizeChanged += BubbleHost_SizeChanged;
        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(48) };
        _bubbleTimer.Tick += (_, _) => TickBubbles(host, progressBar);
        _bubbleTimer.Start();
        TickBubbles(host, progressBar);
    }

    private static void BubbleHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Panel host && _bubbleCanvas is not null)
        {
            TickBubbles(host, _trackedProgressBar);
        }
    }

    private static void TickBubbles(Panel host, ProgressBar? progressBar)
    {
        if (_bubbleCanvas is null || _bubbles.Count == 0)
        {
            return;
        }

        var w = host.ActualWidth;
        var h = host.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var fillEnd = progressBar is null ? w : Math.Max(8, w * Math.Clamp(progressBar.Value, 0, 100) / 100.0);
        foreach (var b in _bubbles)
        {
            b.Phase += b.RiseSpeed;
            var bob = Math.Sin(b.Phase);
            var x = (b.BaseX * fillEnd) + (bob * 3.0) - (b.Shape.Width * 0.5);
            var y = (h * 0.5) + (bob * 2.5) - (b.Shape.Height * 0.5) - Math.Abs(Math.Sin(b.Phase * 0.6)) * 1.5;
            Canvas.SetLeft(b.Shape, Math.Clamp(x, 0, Math.Max(0, w - b.Shape.Width)));
            Canvas.SetTop(b.Shape, Math.Clamp(y, 0, Math.Max(0, h - b.Shape.Height)));
            b.Shape.Opacity = 0.25 + ((bob + 1) * 0.22);
        }
    }

    private static void StopBubbles()
    {
        StopDispatcherTimer(ref _bubbleTimer);

        if (_bubbleHost is not null)
        {
            _bubbleHost.SizeChanged -= BubbleHost_SizeChanged;
            if (_bubbleCanvas is not null)
            {
                _bubbleHost.Children.Remove(_bubbleCanvas);
            }
        }

        _bubbleCanvas = null;
        _bubbles.Clear();
        _trackedProgressBar = null;
    }

    private static void StopDispatcherTimer(ref DispatcherTimer? timer)
    {
        if (timer is null)
        {
            return;
        }

        try
        {
            timer.Stop();
        }
        catch (COMException)
        {
            // MediaEnded / theme transitions can arrive off the UI thread.
        }

        timer = null;
    }

    private static void AnimateThemeIn(ProgressBar progressBar)
    {
        var anim = new DoubleAnimation
        {
            From = 0.85,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(380)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, progressBar);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static void PulseRestore(ProgressBar progressBar)
    {
        var anim = new DoubleAnimation
        {
            From = progressBar.Opacity,
            To = Math.Min(1, progressBar.Opacity + 0.08),
            Duration = new Duration(TimeSpan.FromMilliseconds(140)),
            AutoReverse = true,
            EnableDependentAnimation = true,
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, progressBar);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            255,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
