using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace GSBT.WinUI.Services;

/// <summary>Fluent <see cref="ContentDialog"/> show helper with a short open animation (scale + fade).</summary>
public static class GsbtContentDialog
{
    private const int OpenDurationMs = 220;

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var loadedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? onLoaded = null;
        onLoaded = (_, _) =>
        {
            dialog.Loaded -= onLoaded;
            loadedTcs.TrySetResult();
        };

        dialog.Loaded += onLoaded;
        dialog.Opacity = 0;

        var scale = new CompositeTransform { ScaleX = 0.96, ScaleY = 0.96 };
        dialog.RenderTransform = scale;
        dialog.RenderTransformOrigin = new Point(0.5, 0.5);

        void OnClosed(ContentDialog _, ContentDialogClosedEventArgs __)
        {
            dialog.Closed -= OnClosed;
            dialog.ClearValue(UIElement.OpacityProperty);
            dialog.RenderTransform = null;
        }

        dialog.Closed += OnClosed;

        var showTask = dialog.ShowAsync().AsTask();

        if (dialog.IsLoaded)
        {
            loadedTcs.TrySetResult();
        }
        else
        {
            await loadedTcs.Task.ConfigureAwait(true);
        }

        await PlayOpenAnimationAsync(dialog, scale).ConfigureAwait(true);
        return await showTask.ConfigureAwait(true);
    }

    private static async Task PlayOpenAnimationAsync(UIElement target, CompositeTransform scale)
    {
        var duration = TimeSpan.FromMilliseconds(OpenDurationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();

        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(opacity, target);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        sb.Children.Add(opacity);

        var scaleX = new DoubleAnimation
        {
            From = 0.96,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleX, scale);
        Storyboard.SetTargetProperty(scaleX, "ScaleX");
        sb.Children.Add(scaleX);

        var scaleY = new DoubleAnimation
        {
            From = 0.96,
            To = 1,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(scaleY, scale);
        Storyboard.SetTargetProperty(scaleY, "ScaleY");
        sb.Children.Add(scaleY);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(object? s, object e)
        {
            sb.Completed -= OnDone;
            tcs.TrySetResult();
        }

        sb.Completed += OnDone;
        sb.Begin();

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(OpenDurationMs + 80)).ConfigureAwait(true);
        if (!ReferenceEquals(finished, tcs.Task))
        {
            try
            {
                sb.Stop();
            }
            catch
            {
                // ignore
            }
        }

        target.Opacity = 1;
        scale.ScaleX = 1;
        scale.ScaleY = 1;
    }
}
