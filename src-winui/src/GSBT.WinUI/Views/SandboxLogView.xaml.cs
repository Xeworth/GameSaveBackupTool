using System.Collections.Generic;
using System.Linq;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Views;

public sealed partial class SandboxLogView : UserControl
{
    private const int MaxDisplayChars = 260_000;
    private const int MaxInitialLines = 2_500;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(120);

    private readonly SandboxLogHub _hub;
    private readonly object _pendingLock = new();
    private readonly List<string> _pending = new();
    private Microsoft.UI.Xaml.DispatcherTimer? _flushTimer;

    public SandboxLogView(SandboxLogHub hub)
    {
        _hub = hub;
        InitializeComponent();

        RebuildFromHub();
        hub.LineAppended += HubOnLineAppended;
        hub.PreferencesChanged += HubOnPreferencesChanged;
        hub.Cleared += HubOnCleared;
        Unloaded += SandboxLogView_Unloaded;
    }

    private void SandboxLogView_Unloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _hub.LineAppended -= HubOnLineAppended;
        _hub.PreferencesChanged -= HubOnPreferencesChanged;
        _hub.Cleared -= HubOnCleared;
        Unloaded -= SandboxLogView_Unloaded;
        StopFlushTimer();
    }

    private void HubOnPreferencesChanged()
    {
        _ = DispatcherQueue.TryEnqueue(RebuildFromHub);
    }

    private void HubOnCleared()
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            StopFlushTimer();
            lock (_pendingLock)
            {
                _pending.Clear();
            }

            LogBox.Text = string.Empty;
        });
    }

    private void RebuildFromHub()
    {
        StopFlushTimer();
        lock (_pendingLock)
        {
            _pending.Clear();
        }

        ApplySnapshotText(BuildSnapshotText());
    }

    private void StopFlushTimer()
    {
        if (_flushTimer is null)
        {
            return;
        }

        _flushTimer.Stop();
        _flushTimer.Tick -= FlushTimer_Tick;
        _flushTimer = null;
    }

    private string BuildSnapshotText()
    {
        var snap = _hub.Snapshot();
        if (snap.Count == 0)
        {
            return string.Empty;
        }

        var slice = snap.Count > MaxInitialLines
            ? snap.Skip(snap.Count - MaxInitialLines)
            : snap;
        var text = string.Join(Environment.NewLine, slice.Select(static x => x.Line));
        if (text.Length > MaxDisplayChars)
        {
            text = text[^MaxDisplayChars..];
        }

        return text;
    }

    private void ApplySnapshotText(string text)
    {
        LogBox.Text = text;
        if (LogBox.Text.Length > 0)
        {
            LogBox.Select(LogBox.Text.Length, 0);
        }
    }

    private void HubOnLineAppended(string category, string line)
    {
        _ = DispatcherQueue.TryEnqueue(() => EnqueuePendingLine(line));
    }

    private void EnqueuePendingLine(string line)
    {
        lock (_pendingLock)
        {
            _pending.Add(line);
        }

        if (_flushTimer is null)
        {
            _flushTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = FlushInterval };
            _flushTimer.Tick += FlushTimer_Tick;
            _flushTimer.Start();
        }
    }

    private void FlushTimer_Tick(object? sender, object e)
    {
        List<string>? batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                StopFlushTimer();
                return;
            }

            batch = new List<string>(_pending);
            _pending.Clear();
        }

        var add = string.Join(Environment.NewLine, batch);
        var sep = LogBox.Text.Length == 0 ? string.Empty : Environment.NewLine;
        var combined = LogBox.Text + sep + add;
        if (combined.Length > MaxDisplayChars)
        {
            combined = combined[^MaxDisplayChars..];
        }

        LogBox.Text = combined;
        LogBox.Select(LogBox.Text.Length, 0);
    }
}
