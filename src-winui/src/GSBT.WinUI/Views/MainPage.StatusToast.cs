using System.Threading;
using GSBT.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Dispatching;

namespace GSBT.WinUI.Views;

/// <summary>
/// In-app status rows (ephemeral stack, max 3) above a single integrity chrome toast. OS tray is unchanged in <see cref="MainViewModel"/>.
/// </summary>
public partial class MainPage
{
    private const int MaxEphemeralStatusRows = 3;

    private CancellationTokenSource? _statusToastCts;
    private Task? _statusToastSequenceTask;
    private readonly SemaphoreSlim _statusToastGate = new(1, 1);
    private readonly SemaphoreSlim _ephemeralStatusGate = new(1, 1);
    private int _statusToastChromeDepth;
    private bool _toastSurfaceOverSettings;
    private readonly List<EphemeralToastEntry> _ephemeralEntries = new();
    private DispatcherQueue? _toastDispatcher;

    private DispatcherQueue ToastDispatcher =>
        _toastDispatcher ??= DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Status toasts require a DispatcherQueue.");

    private void WireViewModelStatusToasts()
    {
        _toastDispatcher = DispatcherQueue.GetForCurrentThread();
        ViewModel.NotifyAutoBackupTip = payload =>
        {
            var chrome = payload.Chrome;
            var ms = chrome != AutoBackupToastChrome.None ? Math.Max(2600, 16000) : 2600;
            _ = ShowStatusToastAsync(
                payload.Message,
                ms,
                chrome,
                payload.MessageSecondLine,
                payload.Severity);
        };

        ViewModel.FlushPendingAutoBackupTip();
    }

    private async Task CleanupStatusToastOnUnloadAsync()
    {
        await ClearAllEphemeralToastsForUnloadAsync().ConfigureAwait(true);
        await CancelStatusToastAndAwaitAsync().ConfigureAwait(true);
        try
        {
            _statusToastGate.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _ephemeralStatusGate.Dispose();
        }
        catch
        {
            // ignore
        }
    }

    private void StatusToastDismiss_Click(object sender, RoutedEventArgs e) => CancelStatusToastTokensOnly();

    private void StatusToastClearHighlights_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearLastBackupIntegrityWarnings();
        CancelStatusToastTokensOnly();
    }

    internal Task ShowStatusToastAsync(
        string message,
        int? visibleMsOverride = null,
        AutoBackupToastChrome toastChrome = AutoBackupToastChrome.None,
        string? messageSecondLine = null,
        BackupToastSeverity severity = BackupToastSeverity.Neutral)
    {
        if (toastChrome != AutoBackupToastChrome.None)
        {
            if (!_settingsStore.Get("in_app_backup_warnings_enabled", true))
            {
                return Task.CompletedTask;
            }
        }
        else if (!_settingsStore.Get("in_app_ephemeral_status_enabled", true))
        {
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var trimmed = message.Trim();
        if (toastChrome != AutoBackupToastChrome.None)
        {
            return RunChromeToastPipelineAsync(trimmed, visibleMsOverride, toastChrome, messageSecondLine, severity);
        }

        return PushEphemeralStatusAsync(trimmed, visibleMsOverride, messageSecondLine);
    }

    private int GetDefaultStatusToastMilliseconds() =>
        Math.Clamp(_settingsStore.Get("status_message_duration_seconds", 3), 1, 5) * 1000;

    private sealed class EphemeralToastEntry
    {
        public required Guid Id { get; init; }
        public required string Line1 { get; init; }
        public string? Line2 { get; init; }
        public CancellationTokenSource DismissCts { get; } = new();
    }

    private static Border[] EphemeralBorders(MainPage p) =>
    [
        p.EphemeralToast0Border,
        p.EphemeralToast1Border,
        p.EphemeralToast2Border,
    ];

    private static TextBlock[] EphemeralLine1(MainPage p) =>
    [
        p.EphemeralToast0Line1,
        p.EphemeralToast1Line1,
        p.EphemeralToast2Line1,
    ];

    private static TextBlock[] EphemeralLine2(MainPage p) =>
    [
        p.EphemeralToast0Line2,
        p.EphemeralToast1Line2,
        p.EphemeralToast2Line2,
    ];

    private static TranslateTransform[] EphemeralTransforms(MainPage p) =>
    [
        p.EphemeralToast0Transform,
        p.EphemeralToast1Transform,
        p.EphemeralToast2Transform,
    ];

    internal bool IsAnyEphemeralToastVisible()
    {
        foreach (var b in EphemeralBorders(this))
        {
            if (b.Visibility == Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }

    internal async Task ClearAllEphemeralToastsForEscapeAsync()
    {
        await _ephemeralStatusGate.WaitAsync().ConfigureAwait(true);
        try
        {
            foreach (var e in _ephemeralEntries)
            {
                try
                {
                    e.DismissCts.Cancel();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    e.DismissCts.Dispose();
                }
                catch
                {
                    // ignore
                }
            }

            _ephemeralEntries.Clear();
            await FadeOutAllEphemeralSlotsAsync().ConfigureAwait(true);
            HideAllEphemeralSlots();
            TryResetToastStackZOrder();
        }
        finally
        {
            try
            {
                _ephemeralStatusGate.Release();
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task ClearAllEphemeralToastsForUnloadAsync()
    {
        try
        {
            await ClearAllEphemeralToastsForEscapeAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }
    }

    private async Task PushEphemeralStatusAsync(
        string message,
        int? visibleMsOverride,
        string? messageSecondLine)
    {
        var delayMs = Math.Max(800, visibleMsOverride ?? GetDefaultStatusToastMilliseconds());
        await _ephemeralStatusGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_ephemeralEntries.Count == MaxEphemeralStatusRows)
            {
                var evicted = _ephemeralEntries[0];
                _ephemeralEntries.RemoveAt(0);
                try
                {
                    evicted.DismissCts.Cancel();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    evicted.DismissCts.Dispose();
                }
                catch
                {
                    // ignore
                }

                await AnimateFadeOutEphemeralSlotAsync(0).ConfigureAwait(true);
            }

            var entry = new EphemeralToastEntry
            {
                Id = Guid.NewGuid(),
                Line1 = message,
                Line2 = string.IsNullOrWhiteSpace(messageSecondLine) ? null : messageSecondLine.Trim(),
            };
            _ephemeralEntries.Add(entry);
            ApplyEphemeralTextsFromList();

            if (_settingsOpen)
            {
                _toastSurfaceOverSettings = true;
                Canvas.SetZIndex(BottomMessageStack, 20);
            }

            var newestSlot = EphemeralSlotIndexForListIndex(_ephemeralEntries.Count - 1, _ephemeralEntries.Count);
            await AnimateFadeSlideInEphemeralAsync(newestSlot).ConfigureAwait(true);
            ScheduleEphemeralDismiss(entry.Id, delayMs);
        }
        finally
        {
            try
            {
                _ephemeralStatusGate.Release();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static int EphemeralSlotIndexForListIndex(int listIndex, int count) =>
        listIndex + (MaxEphemeralStatusRows - count);

    private void ApplyEphemeralTextsFromList()
    {
        var count = _ephemeralEntries.Count;
        var borders = EphemeralBorders(this);
        var lines1 = EphemeralLine1(this);
        var lines2 = EphemeralLine2(this);
        var transforms = EphemeralTransforms(this);
        var newestSlot = count > 0 ? EphemeralSlotIndexForListIndex(count - 1, count) : -1;

        for (var slot = 0; slot < MaxEphemeralStatusRows; slot++)
        {
            var listIndex = slot - (MaxEphemeralStatusRows - count);
            if (listIndex < 0 || listIndex >= count)
            {
                borders[slot].Visibility = Visibility.Collapsed;
                borders[slot].Opacity = 0;
                transforms[slot].Y = 0;
                continue;
            }

            var entry = _ephemeralEntries[listIndex];
            lines1[slot].Text = entry.Line1;
            if (!string.IsNullOrEmpty(entry.Line2))
            {
                lines2[slot].Text = entry.Line2;
                lines2[slot].Visibility = Visibility.Visible;
            }
            else
            {
                lines2[slot].Visibility = Visibility.Collapsed;
            }

            borders[slot].Visibility = Visibility.Visible;
            if (slot != newestSlot)
            {
                borders[slot].Opacity = 1;
                transforms[slot].Y = 0;
            }
        }
    }

    private void ScheduleEphemeralDismiss(Guid id, int delayMs)
    {
        var dq = ToastDispatcher;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            dq.TryEnqueue(() => _ = RemoveEphemeralByIdAsync(id));
        });
    }

    private async Task RemoveEphemeralByIdAsync(Guid id)
    {
        await _ephemeralStatusGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var idx = _ephemeralEntries.FindIndex(e => e.Id == id);
            if (idx < 0)
            {
                return;
            }

            var countBeforeRemove = _ephemeralEntries.Count;
            var slotToFade = EphemeralSlotIndexForListIndex(idx, countBeforeRemove);
            var entry = _ephemeralEntries[idx];
            _ephemeralEntries.RemoveAt(idx);
            try
            {
                entry.DismissCts.Cancel();
            }
            catch
            {
                // ignore
            }

            try
            {
                entry.DismissCts.Dispose();
            }
            catch
            {
                // ignore
            }

            await AnimateFadeOutEphemeralSlotAsync(slotToFade).ConfigureAwait(true);
            ApplyEphemeralTextsFromList();
            TryResetToastStackZOrder();
        }
        finally
        {
            try
            {
                _ephemeralStatusGate.Release();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void HideAllEphemeralSlots()
    {
        foreach (var b in EphemeralBorders(this))
        {
            b.Visibility = Visibility.Collapsed;
            b.Opacity = 0;
        }

        foreach (var t in EphemeralTransforms(this))
        {
            t.Y = 0;
        }
    }

    private async Task FadeOutAllEphemeralSlotsAsync()
    {
        var tasks = new List<Task>();
        foreach (var b in EphemeralBorders(this))
        {
            if (b.Visibility == Visibility.Visible)
            {
                tasks.Add(AnimateOpacityAsync(b, b.Opacity, 0, 160));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
    }

    private async Task AnimateFadeOutEphemeralSlotAsync(int slotIndex)
    {
        var b = EphemeralBorders(this)[slotIndex];
        if (b.Visibility != Visibility.Visible)
        {
            return;
        }

        await AnimateOpacityAsync(b, b.Opacity, 0, 200).ConfigureAwait(true);
    }

    private async Task AnimateFadeSlideInEphemeralAsync(int slotIndex)
    {
        var b = EphemeralBorders(this)[slotIndex];
        var t = EphemeralTransforms(this)[slotIndex];
        b.Visibility = Visibility.Visible;
        b.Opacity = 0;
        t.Y = 10;
        await Task.WhenAll(
                AnimateOpacityAsync(b, 0, 1, 200),
                AnimateTranslateYAsync(b, t, 10, 0, 200, opacityHold: null))
            .ConfigureAwait(true);
    }

    private async Task RunChromeToastPipelineAsync(
        string message,
        int? visibleMsOverride,
        AutoBackupToastChrome toastChrome,
        string? messageSecondLine,
        BackupToastSeverity severity)
    {
        await _statusToastGate.WaitAsync().ConfigureAwait(true);
        TaskCompletionSource? sequenceDone = null;
        try
        {
            var prev = _statusToastSequenceTask;
            _statusToastSequenceTask = null;
            CancelStatusToastTokensOnly();

            if (prev is not null)
            {
                try
                {
                    await prev.ConfigureAwait(true);
                }
                catch
                {
                    // ignore
                }
            }

            sequenceDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _statusToastSequenceTask = sequenceDone.Task;

            var cts = new CancellationTokenSource();
            _statusToastCts = cts;
            var token = cts.Token;
            Interlocked.Increment(ref _statusToastChromeDepth);

            try
            {
                await PresentChromeToastAsync(
                        message,
                        visibleMsOverride,
                        toastChrome,
                        messageSecondLine,
                        severity,
                        token)
                    .ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Decrement(ref _statusToastChromeDepth);

                try
                {
                    cts.Dispose();
                }
                catch
                {
                    // ignore
                }

                if (ReferenceEquals(_statusToastCts, cts))
                {
                    _statusToastCts = null;
                }
            }
        }
        finally
        {
            try
            {
                _statusToastGate.Release();
            }
            catch
            {
                // ignore
            }

            sequenceDone?.TrySetResult();
        }
    }

    private async Task PresentChromeToastAsync(
        string message,
        int? visibleMsOverride,
        AutoBackupToastChrome toastChrome,
        string? messageSecondLine,
        BackupToastSeverity severity,
        CancellationToken token)
    {
        try
        {
            StatusToastLine1.Text = message;
            if (!string.IsNullOrWhiteSpace(messageSecondLine))
            {
                StatusToastLine2.Text = messageSecondLine.Trim();
                StatusToastDetailsToggle.Visibility = Visibility.Visible;
                StatusToastDetailsToggle.Content = "\u25BC";
                StatusToastLine2.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusToastLine2.Text = string.Empty;
                StatusToastDetailsToggle.Visibility = Visibility.Collapsed;
                StatusToastLine2.Visibility = Visibility.Collapsed;
            }

            StatusToastActionsRow.Visibility = Visibility.Visible;
            StatusToastDismissButton.Visibility = Visibility.Visible;
            StatusToastClearHighlightsButton.Visibility =
                toastChrome == AutoBackupToastChrome.DismissAndClearHighlights ? Visibility.Visible : Visibility.Collapsed;

            ApplyToastSeverityGlyph(severity);

            try
            {
                StatusToastBorder.ClearValue(FrameworkElement.MaxHeightProperty);
            }
            catch
            {
                // ignore
            }

            if (_settingsOpen)
            {
                _toastSurfaceOverSettings = true;
                Canvas.SetZIndex(BottomMessageStack, 20);
            }

            if (ViewModel.BackupIntegrityStripVisible
                && BackupIntegrityStripBorder.Visibility == Visibility.Visible
                && BackupIntegrityStripBorder.Opacity > 0.01)
            {
                try
                {
                    await Task.Delay(110, token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            StatusToastBorder.Visibility = Visibility.Visible;
            StatusToastBorder.Opacity = 0;
            StatusToastTransform.Y = 12;
            await Task.WhenAll(
                    AnimateOpacityAsync(StatusToastBorder, 0, 1, 220),
                    AnimateTranslateYAsync(StatusToastBorder, StatusToastTransform, 12, 0, 220, opacityHold: null))
                .ConfigureAwait(true);

            var visibleMs = visibleMsOverride ?? GetDefaultStatusToastMilliseconds();
            visibleMs = Math.Max(visibleMs, 16000);
            try
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            try
            {
                await Task.WhenAll(
                        AnimateOpacityAsync(StatusToastBorder, StatusToastBorder.Opacity, 0, 240),
                        AnimateTranslateYAsync(StatusToastBorder, StatusToastTransform, StatusToastTransform.Y, 10, 240, opacityHold: null))
                    .ConfigureAwait(true);
            }
            catch
            {
                // ignore
            }

            StatusToastActionsRow.Visibility = Visibility.Collapsed;
            StatusToastDetailsToggle.Visibility = Visibility.Collapsed;
            StatusToastDetailsToggle.Content = "\u25BC";
            StatusToastLine2.Visibility = Visibility.Collapsed;
            StatusToastLine2.Text = string.Empty;
            StatusToastSeverityGlyph.Visibility = Visibility.Collapsed;
            StatusToastBorder.Visibility = Visibility.Collapsed;
            StatusToastBorder.Opacity = 0;
            StatusToastTransform.Y = 8;
            try
            {
                StatusToastBorder.ClearValue(FrameworkElement.MaxHeightProperty);
            }
            catch
            {
                // ignore
            }

            TryResetToastStackZOrder();
        }
    }

    private void StatusToastDetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (StatusToastLine2.Visibility == Visibility.Visible)
        {
            StatusToastLine2.Visibility = Visibility.Collapsed;
            StatusToastDetailsToggle.Content = "\u25BC";
        }
        else
        {
            StatusToastLine2.Visibility = Visibility.Visible;
            StatusToastDetailsToggle.Content = "\u25B2";
        }
    }

    private void ApplyToastSeverityGlyph(BackupToastSeverity severity)
    {
        switch (severity)
        {
            case BackupToastSeverity.Warning:
                StatusToastSeverityGlyph.Visibility = Visibility.Visible;
                StatusToastSeverityGlyph.Text = "\u26A0";
                StatusToastSeverityGlyph.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
                break;
            case BackupToastSeverity.Error:
                StatusToastSeverityGlyph.Visibility = Visibility.Visible;
                StatusToastSeverityGlyph.Text = "\u274C";
                StatusToastSeverityGlyph.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
                break;
            default:
                StatusToastSeverityGlyph.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void TryResetToastStackZOrder()
    {
        if (!_toastSurfaceOverSettings)
        {
            return;
        }

        var chromeOpen = StatusToastBorder.Visibility == Visibility.Visible;
        if (!IsAnyEphemeralToastVisible() && !chromeOpen)
        {
            _toastSurfaceOverSettings = false;
            Canvas.SetZIndex(BottomMessageStack, 1);
        }
    }

    private static Task RunStoryboardAsync(Storyboard storyboard)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(object? s, object e)
        {
            storyboard.Completed -= OnDone;
            tcs.TrySetResult();
        }

        storyboard.Completed += OnDone;
        storyboard.Begin();
        return tcs.Task;
    }

    private static Task AnimateOpacityAsync(FrameworkElement target, double from, double to, int durationMs)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Children.Add(anim);
        return RunStoryboardAsync(sb);
    }

    private static Task AnimateTranslateYAsync(
        FrameworkElement target,
        TranslateTransform transform,
        double from,
        double to,
        int durationMs,
        double? opacityHold)
    {
        if (opacityHold is not null)
        {
            target.Opacity = opacityHold.Value;
        }

        transform.Y = from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true,
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, transform);
        Storyboard.SetTargetProperty(anim, "Y");
        sb.Children.Add(anim);
        return RunStoryboardAsync(sb);
    }

    private void CancelStatusToastTokensOnly()
    {
        if (_statusToastCts is null)
        {
            return;
        }

        try
        {
            _statusToastCts.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    private async Task CancelStatusToastAndAwaitAsync()
    {
        var prev = _statusToastSequenceTask;
        _statusToastSequenceTask = null;
        CancelStatusToastTokensOnly();

        if (prev is not null)
        {
            try
            {
                await prev.ConfigureAwait(true);
            }
            catch
            {
                // ignore
            }
        }

        if (_statusToastCts is not null)
        {
            try
            {
                _statusToastCts.Dispose();
            }
            catch
            {
                // ignore
            }

            _statusToastCts = null;
        }
    }
}
