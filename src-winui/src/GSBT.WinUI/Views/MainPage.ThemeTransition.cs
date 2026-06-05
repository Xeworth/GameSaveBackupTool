using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace GSBT.WinUI.Views;

/// <summary>
/// Theme switch animations for the main shell (<see cref="ShellRoot"/>).
/// <see cref="ThemeBridge.ApplyFromUiThemeKey"/> + <see cref="SettingsPanel.SyncShellThemeFromMainPage"/> run at the dim valley (soft composite) or between region dim and lift (staggered).
/// </summary>
public partial class MainPage
{
    private enum ThemeShellTransitionStyle
    {
        /// <summary>Dim → apply → staggered lift on a few large regions (not per-card; lighter on the UI thread).</summary>
        StaggeredPulse,

        /// <summary>Opacity + scale + translate on <see cref="ShellRoot"/> only — smoothest path (one surface, sine easing).</summary>
        SoftComposite,

        /// <summary>Full shell fade; can strobe when page backdrop and shell go out of sync.</summary>
        FullOpacityFade,
    }

    /// <summary>Switch style here to compare behaviors without git churn.</summary>
    private static readonly ThemeShellTransitionStyle ShellThemeTransitionStyle = ThemeShellTransitionStyle.SoftComposite;

    private readonly SemaphoreSlim _shellThemeTransitionLock = new(1, 1);

    internal async Task ApplyUiThemeWithShellSoftTransitionAsync(string normalizedUiThemeKey)
    {
        await _shellThemeTransitionLock.WaitAsync();
        try
        {
            if (!ShellThemeAnimationsPreferred() || ShellRoot is null)
            {
                ThemeBridge.ApplyFromUiThemeKey(normalizedUiThemeKey);
                GamesTable.RefreshThemeVisuals();
                _settingsPanel?.SyncShellThemeFromMainPage();
                return;
            }

            switch (ShellThemeTransitionStyle)
            {
                case ThemeShellTransitionStyle.StaggeredPulse:
                    await RunStaggeredPulseShellThemeTransitionAsync(normalizedUiThemeKey);
                    break;
                case ThemeShellTransitionStyle.SoftComposite:
                    await RunSoftCompositeShellThemeTransitionAsync(normalizedUiThemeKey);
                    break;
                case ThemeShellTransitionStyle.FullOpacityFade:
                    await RunFullOpacityFadeShellThemeTransitionAsync(normalizedUiThemeKey);
                    break;
            }
        }
        finally
        {
            SyncSandboxMonitorChromeTheme();
            _shellThemeTransitionLock.Release();
        }
    }

    /// <summary>
    /// Light region wave: only a few large surfaces (not every settings card) so dependent opacity animations
    /// do not stutter; theme still applies in one frame between dim and lift.
    /// </summary>
    private async Task RunStaggeredPulseShellThemeTransitionAsync(string normalizedUiThemeKey)
    {
        ResetThemeShellMotionIdentity();
        ShellRoot.Opacity = 1;

        var targets = BuildThemeStaggerTargets();
        if (targets.Count == 0)
        {
            ThemeBridge.ApplyFromUiThemeKey(normalizedUiThemeKey);
            GamesTable.RefreshThemeVisuals();
            _settingsPanel?.SyncShellThemeFromMainPage();
            return;
        }

        const double dimFactor = 0.83;
        const int dimMs = 360;
        const int staggerMs = 52;
        const int liftMs = 440;
        var easeDim = new SineEase { EasingMode = EasingMode.EaseInOut };
        var easeLift = new SineEase { EasingMode = EasingMode.EaseOut };

        var dimLines = targets
            .Select(t => ((DependencyObject)t.Fe, "Opacity", t.RestoreOpacity, t.RestoreOpacity * dimFactor))
            .ToArray();
        await RunParallelDoubleAnimationsAsync(dimMs, easeDim, dimLines);

        ThemeBridge.ApplyFromUiThemeKey(normalizedUiThemeKey);
        GamesTable.RefreshThemeVisuals();
        _settingsPanel?.SyncShellThemeFromMainPage();

        var sb = new Storyboard();
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var fromDim = t.RestoreOpacity * dimFactor;
            var anim = new DoubleAnimation
            {
                From = fromDim,
                To = t.RestoreOpacity,
                Duration = TimeSpan.FromMilliseconds(liftMs),
                BeginTime = TimeSpan.FromMilliseconds(i * staggerMs),
                EnableDependentAnimation = true,
                EasingFunction = easeLift,
            };
            Storyboard.SetTarget(anim, t.Fe);
            Storyboard.SetTargetProperty(anim, "Opacity");
            sb.Children.Add(anim);
        }

        if (sb.Children.Count > 0)
        {
            await RunStoryboardAsync(sb).ConfigureAwait(true);
        }

        foreach (var t in targets)
            t.Fe.Opacity = t.RestoreOpacity;
    }

    /// <summary>Major shell bands only — avoids dozens of parallel opacity animations on settings cards (janky).</summary>
    private List<(FrameworkElement Fe, double RestoreOpacity)> BuildThemeStaggerTargets()
    {
        var list = new List<(FrameworkElement Fe, double RestoreOpacity)>();

        void TryAdd(FrameworkElement? fe)
        {
            if (fe is null)
            {
                return;
            }

            if (fe.Visibility != Visibility.Visible)
            {
                return;
            }

            var o = fe.Opacity;
            if (o < 0.02)
            {
                return;
            }

            list.Add((fe, o));
        }

        TryAdd(MainContentArea);
        TryAdd(SettingsPanelContainer);
        TryAdd(ProgressStripGrid);
        TryAdd(NormalFooterBorder);
        TryAdd(SettingsFooterBorder);

        return list;
    }

    /// <summary>Full shell fade out → theme apply (synced) → fade in.</summary>
    private async Task RunFullOpacityFadeShellThemeTransitionAsync(string normalizedUiThemeKey)
    {
        ResetThemeShellMotionIdentity();

        const int fadeOutMs = 380;
        const int fadeInMs = 480;
        var easeOut = new SineEase { EasingMode = EasingMode.EaseInOut };
        var easeIn = new SineEase { EasingMode = EasingMode.EaseOut };

        ShellRoot.Opacity = Math.Clamp(ShellRoot.Opacity, 0, 1);
        await RunParallelDoubleAnimationsAsync(
            fadeOutMs,
            easeOut,
            (ShellRoot, "Opacity", ShellRoot.Opacity, 0.0));

        ThemeBridge.ApplyFromUiThemeKey(normalizedUiThemeKey);
        GamesTable.RefreshThemeVisuals();
        _settingsPanel?.SyncShellThemeFromMainPage();

        await RunParallelDoubleAnimationsAsync(
            fadeInMs,
            easeIn,
            (ShellRoot, "Opacity", 0.0, 1.0));

        ShellRoot.Opacity = 1;
        ResetThemeShellMotionIdentity();
    }

    /// <summary>Single-surface “breath”: sine in/out on the way down, ease out on the way up (theme applies at the dim valley).</summary>
    private async Task RunSoftCompositeShellThemeTransitionAsync(string normalizedUiThemeKey)
    {
        const double dimOpacity = 0.74;
        const double dimScale = 0.978;
        const double dimDriftY = 5.0;
        const int dimMs = 440;
        const int liftMs = 560;

        var easeDim = new SineEase { EasingMode = EasingMode.EaseInOut };
        var easeLift = new SineEase { EasingMode = EasingMode.EaseOut };

        var motion = ThemeShellMotion;
        if (motion is not null)
        {
            await RunParallelDoubleAnimationsAsync(
                dimMs,
                easeDim,
                (ShellRoot, "Opacity", ShellRoot.Opacity, dimOpacity),
                (motion, "ScaleX", motion.ScaleX, dimScale),
                (motion, "ScaleY", motion.ScaleY, dimScale),
                (motion, "TranslateY", motion.TranslateY, dimDriftY));
        }
        else
        {
            await RunParallelDoubleAnimationsAsync(
                dimMs,
                easeDim,
                (ShellRoot, "Opacity", ShellRoot.Opacity, dimOpacity));
        }

        ThemeBridge.ApplyFromUiThemeKey(normalizedUiThemeKey);
        GamesTable.RefreshThemeVisuals();
        _settingsPanel?.SyncShellThemeFromMainPage();

        if (motion is not null)
        {
            await RunParallelDoubleAnimationsAsync(
                liftMs,
                easeLift,
                (ShellRoot, "Opacity", ShellRoot.Opacity, 1.0),
                (motion, "ScaleX", motion.ScaleX, 1.0),
                (motion, "ScaleY", motion.ScaleY, 1.0),
                (motion, "TranslateY", motion.TranslateY, 0.0));
        }
        else
        {
            await RunParallelDoubleAnimationsAsync(
                liftMs,
                easeLift,
                (ShellRoot, "Opacity", ShellRoot.Opacity, 1.0));
        }

        ShellRoot.Opacity = 1;
        ResetThemeShellMotionIdentity();
    }

    private void ResetThemeShellMotionIdentity()
    {
        if (ThemeShellMotion is { } m)
        {
            m.ScaleX = 1;
            m.ScaleY = 1;
            m.TranslateY = 0;
        }
    }

    private static bool ShellThemeAnimationsPreferred()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private static async Task RunParallelDoubleAnimationsAsync(
        int durationMs,
        EasingFunctionBase easing,
        params (DependencyObject target, string property, double from, double to)[] lines)
    {
        if (lines.Length == 0)
        {
            return;
        }

        var sb = new Storyboard();

        foreach (var (target, property, from, to) in lines)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EnableDependentAnimation = true,
                EasingFunction = easing,
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, property);
            sb.Children.Add(anim);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnComplete(object? s, object e)
        {
            sb.Completed -= OnComplete;
            tcs.TrySetResult();
        }

        sb.Completed += OnComplete;
        sb.Begin();
        await tcs.Task.ConfigureAwait(true);
    }
}
