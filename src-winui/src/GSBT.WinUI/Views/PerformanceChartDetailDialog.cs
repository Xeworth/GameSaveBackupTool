using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;

namespace GSBT.WinUI.Views;

internal static class PerformanceChartDetailDialog
{
    private const double PixelsPerSample = ScrollablePerformanceChartHost.DefaultPixelsPerSample;
    private const double TimeAxisScrollPad = 28;
    private const double ChartMinHeight = 360;
    private const double StatsPanelWidth = 445;
    private const double StatsCardsRowMinWidth = StatsPanelWidth * 2 + 12;
    private static readonly BatchTestCardLayoutOptions DetailBatchCardLayout = new(
        MinCardWidth: 200,
        MaxCardWidth: 280,
        CardGap: 8,
        MaxCardColumns: 16,
        UseFixedWidthColumns: true);
    private static readonly Thickness DetailBodyMargin = new(24, 12, 24, 8);

    public static Task ShowCpuAsync(
        FrameworkElement host,
        Window ownerWindow,
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints) =>
        ShowMetricChartAsync(
            host,
            ownerWindow,
            "CPU usage — detail",
            monitor,
            batchHub,
            settings,
            checkpoints,
            isMemory: false);

    public static Task ShowMemoryAsync(
        FrameworkElement host,
        Window ownerWindow,
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints) =>
        ShowMetricChartAsync(
            host,
            ownerWindow,
            "RAM usage — detail",
            monitor,
            batchHub,
            settings,
            checkpoints,
            isMemory: true);

    public static Task ShowOverlayAsync(
        FrameworkElement host,
        Window ownerWindow,
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints)
    {
        var n = monitor.SampleCount;
        if (n <= 0)
        {
            return Task.CompletedTask;
        }

        var showGsbt = PerformanceChartDisplaySettings.ShowGsbt(settings);
        var showCompress = PerformanceChartDisplaySettings.ShowCompression(settings);
        var dark = ResolveChromeTheme(host) == ElementTheme.Dark;

        var appCpu = new double[n];
        var compressCpu = new double[n];
        var appMem = new double[n];
        var compressMem = new double[n];
        monitor.CopyHistory(appCpu, appMem, compressCpu, compressMem);

        var markers = new List<PerformanceChartCheckpointMarker>();
        monitor.MapStartMarkersToHistory(n, markers);

        var segmentBands = new List<PerformanceChartSegmentBand>();
        monitor.MapSegmentsToHistory(n, segmentBands);

        var activityLabels = new string[n];
        monitor.CopyActivityLabels(activityLabels);

        var chromeCollector = new PerformanceChartDetailChromeCollector();
        var selectionStats = CreateStatsHost(dark);
        var chart = CreateCombinedDetailChart(monitor, markers.Count > 0 ? markers : checkpoints, dark);
        chromeCollector.RegisterChart(chart);
        chart.SampleActivityLabels = activityLabels;
        chart.Series1 = showGsbt ? appCpu : null;
        chart.Series2 = showCompress ? compressCpu : null;
        chart.Series3 = showGsbt ? appMem : null;
        chart.Series4 = showCompress ? compressMem : null;
        chart.Series1Brush = PerformanceChartPalettes.CpuGsbtBrush;
        chart.Series2Brush = PerformanceChartPalettes.CpuCompressionBrush;
        chart.Series3Brush = PerformanceChartPalettes.MemGsbtBrush;
        chart.Series4Brush = PerformanceChartPalettes.MemCompressionBrush;
        chart.PlotMemoryNormalized = true;
        chart.SystemTotalMemoryMb = monitor.SystemTotalPhysicalMemMb;
        chart.MaxValue = 100;
        chart.AutoScale = false;
        chart.ShowPercentGrid = true;
        chart.Series1Label = "GSBT CPU %";
        chart.Series2Label = "Compression CPU %";
        chart.Series3Label = "GSBT RAM";
        chart.Series4Label = "Compression RAM";

        var systemMemMb = monitor.SystemTotalPhysicalMemMb;
        var gameEvents = new List<(long Serial, string Game)>();
        monitor.CopyCompressionGameEvents(gameEvents);
        chart.CompressionGameEvents = gameEvents;

        var body = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        bool IsDetailDark() => ThemeBridge.IsDarkChrome(body);

        var hint = DiagnosticTextBlock(
            "CPU % and RAM as % of installed RAM (dashed). Scroll for the full run; Ctrl + scroll wheel zooms at the cursor; Shift + scroll wheel pans horizontally. Hover for values and the game folder being zipped; drag to select a range. Click test cards to toggle segments; right-click a card to select its run on the chart.",
            dark);

        body.Children.Add(hint);

        PerformanceChartRangeSummary? lastRangeSummary = null;
        chart.RangeSelectionChanged += summary =>
        {
            lastRangeSummary = summary;
            ApplyRangeSummary(
                selectionStats,
                summary,
                includeMemory: true,
                systemMemMb,
                showGsbt,
                showCompress,
                batchHub,
                segmentBands,
                IsDetailDark());
        };

        var scrollHost = new ScrollablePerformanceChartHost(chart, n, PixelsPerSample);

        var batchCtx = BuildBatchCardsSection(
            batchHub,
            chart,
            scrollHost,
            segmentBands,
            dark,
            chromeCollector);
        if (batchCtx?.Section is not null)
        {
            body.Children.Add(batchCtx.Section);
        }

        var combinedCard = BuildCombinedChartCard(
            scrollHost,
            chart,
            settings,
            showGsbt,
            showCompress,
            markers.Count > 0,
            dark);
        body.Children.Add(combinedCard);
        chromeCollector.RegisterExtraRefresh(d =>
        {
            RefreshDiagnosticText(hint, d);
            RefreshChartCardChrome(combinedCard, d);
            if (lastRangeSummary is { } s)
            {
                ApplyRangeSummary(
                    selectionStats,
                    s,
                    includeMemory: true,
                    systemMemMb,
                    showGsbt,
                    showCompress,
                    batchHub,
                    segmentBands,
                    d);
            }
            else
            {
                RefreshStatsHostChrome(selectionStats, d);
            }
        });

        body.Children.Add(WrapCenteredStats(selectionStats));

        var session = new PerformanceChartLiveDetailSession(
            ownerWindow,
            monitor,
            batchHub,
            settings,
            chart,
            scrollHost,
            PerformanceChartDetailMode.Combined,
            checkpoints,
            batchCtx?.SelectedByTest,
            batchCtx?.CardHosts,
            batchCtx?.SyncSegments,
            segmentBands);

        return ShowLargeChartWindowAsync(
            ownerWindow,
            "Combined diagnostics",
            body,
            ResolveChromeTheme(host),
            DetailBodyMargin,
            session,
            chromeCollector);
    }

    private sealed class BatchCardsSectionContext
    {
        public FrameworkElement? Section { get; init; }
        public List<BatchTestCardHost>? CardHosts { get; init; }
        public Dictionary<int, bool>? SelectedByTest { get; init; }
        public Action? SyncSegments { get; init; }
    }

    private static void RefreshChartCardChrome(Border card, bool darkChrome)
    {
        card.Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush");
        card.BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush");
        if (card.Child is not StackPanel inner)
        {
            return;
        }

        var bodyBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        var secondaryBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");

        foreach (var child in inner.Children)
        {
            switch (child)
            {
                case Grid header:
                    foreach (var headerChild in header.Children)
                    {
                        if (headerChild is TextBlock titleTb)
                        {
                            titleTb.Foreground = bodyBrush;
                        }
                    }

                    break;
                case TextBlock captionTb:
                    captionTb.Foreground = secondaryBrush;
                    break;
                case StackPanel legendRow when legendRow.Orientation == Orientation.Horizontal:
                    RefreshChartLegend(legendRow, darkChrome);
                    break;
            }
        }
    }

    private static void RefreshChartLegend(StackPanel legendRow, bool darkChrome)
    {
        var labelBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        foreach (var item in legendRow.Children)
        {
            if (item is not StackPanel itemRow)
            {
                continue;
            }

            foreach (var part in itemRow.Children)
            {
                if (part is TextBlock labelTb)
                {
                    labelTb.Foreground = labelBrush;
                }
            }
        }
    }

    private static void RefreshDiagnosticText(TextBlock text, bool darkChrome) =>
        text.Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");

    private static void RefreshStatsHostChrome(StackPanel statsHost, bool darkChrome)
    {
        var secondary = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");
        var bodyBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        var cardBg = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush");
        var cardBorder = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush");

        foreach (var child in statsHost.Children)
        {
            if (child is TextBlock tb)
            {
                tb.Foreground = secondary;
            }
            else if (child is StackPanel center)
            {
                RefreshStatsCenterChrome(center, darkChrome, secondary, bodyBrush, cardBg, cardBorder);
            }
        }
    }

    private static void RefreshStatsCenterChrome(
        StackPanel center,
        bool darkChrome,
        Brush secondary,
        Brush bodyBrush,
        Brush cardBg,
        Brush cardBorder)
    {
        foreach (var child in center.Children)
        {
            if (child is Border border)
            {
                border.Background = cardBg;
                border.BorderBrush = cardBorder;
                RefreshStatsBorderContent(border, darkChrome, secondary, bodyBrush);
            }
            else if (child is ScrollViewer sv && sv.Content is StackPanel row)
            {
                foreach (var rowChild in row.Children)
                {
                    if (rowChild is Border rowBorder)
                    {
                        rowBorder.Background = cardBg;
                        rowBorder.BorderBrush = cardBorder;
                        RefreshStatsBorderContent(rowBorder, darkChrome, secondary, bodyBrush);
                    }
                }
            }
        }
    }

    private static void RefreshStatsBorderContent(Border border, bool darkChrome, Brush secondary, Brush bodyBrush)
    {
        if (border.Child is Grid grid)
        {
            foreach (var rowChild in grid.Children)
            {
                if (rowChild is TextBlock tb)
                {
                    tb.Foreground = bodyBrush;
                }
            }

            return;
        }

        if (border.Child is StackPanel sp)
        {
            foreach (var spChild in sp.Children)
            {
                if (spChild is TextBlock tb)
                {
                    tb.Foreground = tb.FontWeight == FontWeights.SemiBold ? bodyBrush : secondary;
                }
                else if (spChild is Grid header)
                {
                    foreach (var headerChild in header.Children)
                    {
                        if (headerChild is TextBlock titleTb)
                        {
                            titleTb.Foreground = bodyBrush;
                        }
                    }
                }
            }
        }
    }

    private static BatchCardsSectionContext? BuildBatchCardsSection(
        SandboxBatchPerformanceHub batchHub,
        PerformanceSparkline chart,
        ScrollablePerformanceChartHost scrollHost,
        List<PerformanceChartSegmentBand> segmentBands,
        bool darkChrome,
        PerformanceChartDetailChromeCollector? chromeCollector = null)
    {
        var tests = batchHub.Tests;
        if (tests.Count == 0 || segmentBands.Count == 0)
        {
            return null;
        }

        Action? syncAll = null;

        var segmentTestIndices = segmentBands.Select(b => b.TestIndex).ToHashSet();
        var cardHosts = new List<BatchTestCardHost>();
        var selectedByTest = segmentTestIndices.ToDictionary(i => i, _ => false);

        foreach (var t in tests)
        {
            if (!segmentTestIndices.Contains(t.Index))
            {
                continue;
            }

            var host = BatchTestCardBuilder.Create(
                t,
                darkChrome,
                showProgressBar: true,
                showDetailChartActions: true);
            BatchTestCardBuilder.ApplySegmentSelection(
                host,
                selected: false,
                PerformanceChartPalettes.SegmentColor(t.Index));
            WireDetailChartActions(host, chart, scrollHost, segmentBands);
            cardHosts.Add(host);
        }

        if (cardHosts.Count == 0)
        {
            return null;
        }

        chromeCollector?.RegisterCardHosts(cardHosts);

        var cardsHost = new Grid();
        cardsHost.SizeChanged += (_, _) => BatchTestCardBuilder.RelayoutCards(cardsHost, cardHosts, DetailBatchCardLayout);
        BatchTestCardBuilder.RelayoutCards(cardsHost, cardHosts, DetailBatchCardLayout);

        void SyncAll()
        {
            foreach (var h in cardHosts)
            {
                var on = selectedByTest.TryGetValue(h.Index, out var sel) && sel;
                BatchTestCardBuilder.ApplySegmentSelection(
                    h,
                    on,
                    PerformanceChartPalettes.SegmentColor(h.Index));
            }

            var visible = new List<PerformanceChartSegmentBand>();
            foreach (var band in segmentBands)
            {
                var on = selectedByTest.TryGetValue(band.TestIndex, out var sel) && sel;
                visible.Add(band with { Visible = on });
            }

            chart.TestSegments = visible;
            chart.Redraw();
        }

        syncAll = SyncAll;

        foreach (var h in cardHosts)
        {
            var captured = h;
            captured.Root.PointerPressed += (_, e) =>
            {
                if (!BatchTestCardBuilder.IsPrimaryPointerPressed(e))
                {
                    return;
                }

                var idx = captured.Index;
                var nowOn = !(selectedByTest.TryGetValue(idx, out var sel) && sel);
                selectedByTest[idx] = nowOn;
                SyncAll();
                e.Handled = true;
            };
            captured.Root.RightTapped += (_, e) =>
            {
                SelectTestSegmentOnChart(chart, segmentBands, captured.Index);
                e.Handled = true;
            };
        }

        SyncAll();

        var done = tests.Count(t => t.Phase == BatchTestRunPhase.Completed);
        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new TextBlock
        {
            Text = "Batch run",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
        });
        section.Children.Add(new TextBlock
        {
            Text = batchHub.IsActive
                ? "Step in progress · click a card to toggle its segment · right-click or use Zoom to focus the chart on a test · Reset restores the full run"
                : $"Last batch: {done} completed · click to toggle segments · right-click or Zoom button to focus a test on the chart",
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
        });
        section.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            Content = cardsHost,
        });
        return new BatchCardsSectionContext
        {
            Section = section,
            CardHosts = cardHosts,
            SelectedByTest = selectedByTest,
            SyncSegments = syncAll,
        };
    }

    private static void SelectTestSegmentOnChart(
        PerformanceSparkline chart,
        List<PerformanceChartSegmentBand> segmentBands,
        int testIndex)
    {
        foreach (var band in segmentBands)
        {
            if (band.TestIndex != testIndex)
            {
                continue;
            }

            chart.SetSelectionRange(band.StartIndex, band.EndIndex, raiseEvent: true);
            return;
        }
    }

    private static void WireDetailChartActions(
        BatchTestCardHost host,
        PerformanceSparkline chart,
        ScrollablePerformanceChartHost scrollHost,
        List<PerformanceChartSegmentBand> segmentBands)
    {
        if (host.ZoomToTestButton is not { } zoomBtn)
        {
            return;
        }

        zoomBtn.Click += (_, _) =>
        {
            foreach (var band in segmentBands)
            {
                if (band.TestIndex != host.Index)
                {
                    continue;
                }

                SelectTestSegmentOnChart(chart, segmentBands, host.Index);
                scrollHost.ZoomToSampleRange(band.StartIndex, band.EndIndex);
                return;
            }
        };

        if (host.ResetChartViewButton is { } resetBtn)
        {
            resetBtn.Click += (_, _) =>
            {
                scrollHost.ResetView(ScrollablePerformanceChartHost.DefaultPixelsPerSample);
                chart.ClearSelection();
            };
        }

        foreach (var btn in new[] { zoomBtn, host.ResetChartViewButton })
        {
            if (btn is null)
            {
                continue;
            }

            btn.PointerPressed += (_, e) => e.Handled = true;
        }
    }

    private static Border BuildCombinedChartCard(
        ScrollablePerformanceChartHost scrollHost,
        PerformanceSparkline chart,
        SettingsStore settings,
        bool showGsbt,
        bool showCompress,
        bool hasCheckpoints,
        bool darkChrome)
    {
        var legendItems = new List<(string Label, Color Color, bool Dashed)>();
        if (showGsbt)
        {
            legendItems.Add(("GSBT CPU", PerformanceChartPalettes.CpuGsbt, false));
            legendItems.Add(("GSBT RAM", PerformanceChartPalettes.MemGsbt, true));
        }

        if (showCompress)
        {
            legendItems.Add(("Compression CPU", PerformanceChartPalettes.CpuCompression, false));
            legendItems.Add(("Compression RAM", PerformanceChartPalettes.MemCompression, true));
        }

        if (hasCheckpoints)
        {
            legendItems.Add(("Test start", Color.FromArgb(255, 255, 120, 180), false));
        }

        var caption = new TextBlock
        {
            FontSize = 11,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = "Combined CPU % and RAM (% of installed RAM). Pink markers = test start.",
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Combined",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 0);
        header.Children.Add(title);
        PerformanceChartHeaderChrome.AddHeaderButtons(
            header,
            null,
            chart,
            settings,
            PerformanceChartCheckpointScope.Combined);

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(header);
        inner.Children.Add(caption);
        scrollHost.Height = ChartMinHeight;
        scrollHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        scrollHost.MinHeight = ChartMinHeight;
        inner.Children.Add(scrollHost);
        if (legendItems.Count > 0)
        {
            inner.Children.Add(BuildLegend(darkChrome, legendItems.ToArray()));
        }

        return new Border
        {
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner,
        };
    }

    private static Task ShowMetricChartAsync(
        FrameworkElement host,
        Window ownerWindow,
        string title,
        SandboxResourceMonitor monitor,
        SandboxBatchPerformanceHub batchHub,
        SettingsStore settings,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints,
        bool isMemory)
    {
        var n = monitor.SampleCount;
        if (n <= 0)
        {
            return Task.CompletedTask;
        }

        var showGsbt = PerformanceChartDisplaySettings.ShowGsbt(settings);
        var showCompress = PerformanceChartDisplaySettings.ShowCompression(settings);
        var dark = ResolveChromeTheme(host) == ElementTheme.Dark;

        var s1 = new double[n];
        var s2 = new double[n];
        var appMem = new double[n];
        var compressMem = new double[n];
        monitor.CopyHistory(s1, appMem, s2, compressMem);

        var markers = new List<PerformanceChartCheckpointMarker>();
        monitor.MapStartMarkersToHistory(n, markers);

        var segmentBands = new List<PerformanceChartSegmentBand>();
        monitor.MapSegmentsToHistory(n, segmentBands);

        var activityLabels = new string[n];
        monitor.CopyActivityLabels(activityLabels);

        var chromeCollector = new PerformanceChartDetailChromeCollector();
        var selectionStats = CreateStatsHost(dark);
        var chart = CreateMetricDetailChart(monitor, markers.Count > 0 ? markers : checkpoints, dark);
        chromeCollector.RegisterChart(chart);
        chart.SampleActivityLabels = activityLabels;
        if (isMemory)
        {
            chart.Series1 = showGsbt ? appMem : null;
            chart.Series2 = showCompress ? compressMem : null;
            chart.Series1Brush = PerformanceChartPalettes.MemGsbtBrush;
            chart.Series2Brush = PerformanceChartPalettes.MemCompressionBrush;
            chart.AutoScale = true;
            chart.ShowPercentGrid = false;
            chart.Series1Label = "GSBT RAM";
            chart.Series2Label = "Compression RAM";
        }
        else
        {
            chart.Series1 = showGsbt ? s1 : null;
            chart.Series2 = showCompress ? s2 : null;
            chart.Series1Brush = PerformanceChartPalettes.CpuGsbtBrush;
            chart.Series2Brush = PerformanceChartPalettes.CpuCompressionBrush;
            chart.AutoScale = false;
            chart.ShowPercentGrid = true;
            chart.MaxValue = 100;
            chart.Series1Label = "GSBT CPU %";
            chart.Series2Label = "Compression CPU %";
            chart.RangeMemorySeries1 = showGsbt ? appMem : null;
            chart.RangeMemorySeries2 = showCompress ? compressMem : null;
        }

        var systemMemMb = monitor.SystemTotalPhysicalMemMb;
        var gameEvents = new List<(long Serial, string Game)>();
        monitor.CopyCompressionGameEvents(gameEvents);
        chart.CompressionGameEvents = gameEvents;

        var body = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        bool IsDetailDark() => ThemeBridge.IsDarkChrome(body);

        var hint = DiagnosticTextBlock(
            isMemory
                ? "Scroll horizontally for the full timeline; Ctrl + scroll wheel zooms at the cursor; Shift + scroll wheel pans. Hover for values and the game folder being zipped; drag to select a range."
                : "Scroll horizontally for the full timeline; Ctrl + scroll wheel zooms at the cursor; Shift + scroll wheel pans. Hover for values and the game folder being zipped; drag to select a range.",
            dark);

        body.Children.Add(hint);

        PerformanceChartRangeSummary? lastRangeSummary = null;
        chart.RangeSelectionChanged += summary =>
        {
            lastRangeSummary = summary;
            ApplyRangeSummary(
                selectionStats,
                summary,
                includeMemory: isMemory || chart.RangeMemorySeries1 is not null,
                systemMemMb,
                showGsbt,
                showCompress,
                batchHub,
                segmentBands,
                IsDetailDark());
        };

        var scrollHost = new ScrollablePerformanceChartHost(chart, n, PixelsPerSample);

        var batchCtx = BuildBatchCardsSection(
            batchHub,
            chart,
            scrollHost,
            segmentBands,
            dark,
            chromeCollector);
        if (batchCtx?.Section is not null)
        {
            body.Children.Add(batchCtx.Section);
        }

        var metricCard = BuildMetricChartCard(
            scrollHost,
            chart,
            settings,
            isMemory ? "RAM" : "CPU",
            isMemory,
            showGsbt,
            showCompress,
            markers.Count > 0,
            dark);
        body.Children.Add(metricCard);
        chromeCollector.RegisterExtraRefresh(d =>
        {
            RefreshDiagnosticText(hint, d);
            RefreshChartCardChrome(metricCard, d);
            if (lastRangeSummary is { } s)
            {
                ApplyRangeSummary(
                    selectionStats,
                    s,
                    includeMemory: isMemory || chart.RangeMemorySeries1 is not null,
                    systemMemMb,
                    showGsbt,
                    showCompress,
                    batchHub,
                    segmentBands,
                    d);
            }
            else
            {
                RefreshStatsHostChrome(selectionStats, d);
            }
        });
        body.Children.Add(WrapCenteredStats(selectionStats));

        var session = new PerformanceChartLiveDetailSession(
            ownerWindow,
            monitor,
            batchHub,
            settings,
            chart,
            scrollHost,
            isMemory ? PerformanceChartDetailMode.Memory : PerformanceChartDetailMode.Cpu,
            checkpoints,
            batchCtx?.SelectedByTest,
            batchCtx?.CardHosts,
            batchCtx?.SyncSegments,
            segmentBands);

        return ShowLargeChartWindowAsync(
            ownerWindow,
            title,
            body,
            ResolveChromeTheme(host),
            DetailBodyMargin,
            session,
            chromeCollector);
    }

    private static Border BuildMetricChartCard(
        ScrollablePerformanceChartHost scrollHost,
        PerformanceSparkline chart,
        SettingsStore settings,
        string chartTitle,
        bool isMemory,
        bool showGsbt,
        bool showCompress,
        bool hasCheckpoints,
        bool darkChrome)
    {
        var legendItems = new List<(string Label, Color Color, bool Dashed)>();
        if (isMemory)
        {
            if (showGsbt)
            {
                legendItems.Add(("GSBT RAM", PerformanceChartPalettes.MemGsbt, false));
            }

            if (showCompress)
            {
                legendItems.Add(("Compression RAM", PerformanceChartPalettes.MemCompression, false));
            }
        }
        else
        {
            if (showGsbt)
            {
                legendItems.Add(("GSBT CPU", PerformanceChartPalettes.CpuGsbt, false));
            }

            if (showCompress)
            {
                legendItems.Add(("Compression CPU", PerformanceChartPalettes.CpuCompression, false));
            }
        }

        if (hasCheckpoints)
        {
            legendItems.Add(("Test start", Color.FromArgb(255, 255, 120, 180), false));
        }

        var caption = isMemory
            ? "RAM usage. Pink markers = test start."
            : "CPU utilization (%). Pink markers = test start.";

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new TextBlock
        {
            Text = chartTitle,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(titleBlock, 0);
        header.Children.Add(titleBlock);
        PerformanceChartHeaderChrome.AddHeaderButtons(
            header,
            null,
            chart,
            settings,
            isMemory ? PerformanceChartCheckpointScope.Ram : PerformanceChartCheckpointScope.Cpu);

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(header);
        inner.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
        });
        scrollHost.Height = ChartMinHeight;
        scrollHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        scrollHost.MinHeight = ChartMinHeight;
        inner.Children.Add(scrollHost);
        if (legendItems.Count > 0)
        {
            inner.Children.Add(BuildLegend(darkChrome, legendItems.ToArray()));
        }

        return new Border
        {
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner,
        };
    }

    private static PerformanceSparkline CreateDetailChart(
        SandboxResourceMonitor monitor,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints,
        bool darkChrome)
    {
        var chart = new PerformanceSparkline
        {
            MinHeight = ChartMinHeight,
            ShowTimeAxis = true,
            TimeAxisExtraBottomPad = TimeAxisScrollPad,
            HistoryStartSerial = monitor.HistoryStartSerial,
            SampleIntervalSeconds = SandboxResourceMonitor.ActiveSampleIntervalSeconds,
            EnableHoverInspection = true,
            EnableRangeSelection = true,
            UseDiagnosticChrome = true,
            Checkpoints = checkpoints,
            SystemTotalMemoryMb = monitor.SystemTotalPhysicalMemMb,
        };
        chart.DarkPlotChrome = darkChrome;
        chart.Loaded += (_, _) => chart.Redraw();
        return chart;
    }

    private static PerformanceSparkline CreateCombinedDetailChart(
        SandboxResourceMonitor monitor,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints,
        bool darkChrome)
    {
        var chart = CreateDetailChart(monitor, checkpoints, darkChrome);
        ApplyCardPlotChrome(chart);
        return chart;
    }

    private static PerformanceSparkline CreateMetricDetailChart(
        SandboxResourceMonitor monitor,
        IReadOnlyList<PerformanceChartCheckpointMarker> checkpoints,
        bool darkChrome)
    {
        var chart = CreateDetailChart(monitor, checkpoints, darkChrome);
        ApplyCardPlotChrome(chart);
        return chart;
    }

    private static void ApplyCardPlotChrome(PerformanceSparkline chart)
    {
        chart.UseDiagnosticChrome = false;
        chart.UseCardPlotChrome = true;
    }

    private static FrameworkElement WrapScrollableChart(
        PerformanceSparkline chart,
        int sampleCount,
        bool inCard = false)
    {
        chart.Height = ChartMinHeight;
        chart.HorizontalAlignment = HorizontalAlignment.Stretch;
        var host = new ScrollablePerformanceChartHost(chart, sampleCount, PixelsPerSample)
        {
            MinHeight = ChartMinHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (inCard)
        {
            return host;
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 4),
            Child = host,
        };
    }

    private static FrameworkElement WrapCenteredStats(StackPanel statsHost)
    {
        statsHost.HorizontalAlignment = HorizontalAlignment.Center;
        statsHost.Margin = new Thickness(0, 4, 0, 0);
        return statsHost;
    }

    private static StackPanel CreateStatsHost(bool darkChrome)
    {
        var host = new StackPanel { Spacing = 10 };
        host.Children.Add(new TextBlock
        {
            Text = "Drag on the chart to select a time range and see peak/average stats. Ctrl + scroll wheel zooms at the cursor; Shift + scroll wheel pans. Right-click a test card to select its run.",
            FontSize = 11,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });
        return host;
    }

    private static void ApplyRangeSummary(
        StackPanel host,
        PerformanceChartRangeSummary s,
        bool includeMemory,
        double systemMemMb,
        bool showGsbt,
        bool showCompress,
        SandboxBatchPerformanceHub? batchHub,
        IReadOnlyList<PerformanceChartSegmentBand>? segmentBands,
        bool darkChrome)
    {
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        AddStatsHeader(grid, ref row, $"Selection · {s.SampleCount} samples · {s.DurationSeconds:0.#} s", darkChrome);

        if (showGsbt)
        {
            AddStatsRow(grid, ref row, PerformanceChartPalettes.CpuGsbt, "GSBT CPU",
                $"{s.GsbtCpuMax:0.#}%", $"{s.GsbtCpuAvg:0.#}%", darkChrome);
        }

        if (showCompress)
        {
            AddStatsRow(grid, ref row, PerformanceChartPalettes.CpuCompression, "Compression CPU",
                $"{s.CompressCpuMax:0.#}%", $"{s.CompressCpuAvg:0.#}%", darkChrome);
        }

        if (includeMemory)
        {
            if (showGsbt)
            {
                AddStatsRow(grid, ref row, PerformanceChartPalettes.MemGsbt, "GSBT RAM",
                    FormatMemStat(s.GsbtMemMax, systemMemMb), FormatMemStat(s.GsbtMemAvg, systemMemMb), darkChrome);
            }

            if (showCompress)
            {
                AddStatsRow(grid, ref row, PerformanceChartPalettes.MemCompression, "Compression RAM",
                    FormatMemStat(s.CompressMemMax, systemMemMb), FormatMemStat(s.CompressMemAvg, systemMemMb), darkChrome);
            }
        }

        var selectionCard = new Border
        {
            Width = StatsPanelWidth,
            MaxWidth = StatsPanelWidth,
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = grid,
        };

        var center = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = StatsCardsRowMinWidth,
        };

        var topRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            MinWidth = StatsCardsRowMinWidth,
            MaxWidth = StatsCardsRowMinWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        topRow.Children.Add(selectionCard);

        if (batchHub is not null &&
            segmentBands is { Count: > 0 } &&
            ResolveBestTestIndex(s.StartIndex, s.EndIndex, segmentBands) is int testIdx)
        {
            var entry = batchHub.GetStepResult(testIdx);
            if (entry is not null)
            {
                topRow.Children.Add(BenchmarkResultCardBuilder.BuildCompactCard(entry, darkChrome));
            }
        }

        center.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = topRow,
        });
        if (s.GamesZipped.Count > 0)
        {
            var gamesCard = BuildGamesZippedCard(s.GamesZipped, darkChrome);
            gamesCard.MaxWidth = StatsCardsRowMinWidth;
            gamesCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            center.Children.Add(gamesCard);
        }

        host.Children.Clear();
        host.Children.Add(center);
    }

    private static int? ResolveBestTestIndex(
        int lo,
        int hi,
        IReadOnlyList<PerformanceChartSegmentBand> segmentBands)
    {
        int? bestIdx = null;
        var bestOverlap = 0;
        foreach (var band in segmentBands)
        {
            var start = Math.Max(lo, band.StartIndex);
            var end = Math.Min(hi, band.EndIndex);
            var overlap = end >= start ? end - start + 1 : 0;
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestIdx = band.TestIndex;
            }
        }

        return bestOverlap > 0 ? bestIdx : null;
    }

    private static Border BuildGamesZippedCard(IReadOnlyList<string> games, bool darkChrome)
    {
        var bodyBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        var list = new StackPanel { Spacing = 4 };
        foreach (var game in games)
        {
            list.Children.Add(new TextBlock
            {
                Text = game,
                FontSize = 11,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = bodyBrush,
            });
        }

        var inner = new StackPanel { Spacing = 6 };
        inner.Children.Add(new TextBlock
        {
            Text = "Games zipped",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = bodyBrush,
        });
        inner.Children.Add(list);

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = inner,
        };
    }

    private static string FormatMemStat(double memMiB, double systemMemMb) =>
        PerformanceChartRamFormatter.FormatRamMiB(memMiB, systemMemMb, includePercentOfRam: systemMemMb > 0.001);

    private static void AddStatsHeader(Grid grid, ref int row, string text, bool darkChrome)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(tb, row);
        Grid.SetColumnSpan(tb, 3);
        grid.Children.Add(tb);
        row++;
    }

    private static void AddStatsRow(
        Grid grid,
        ref int row,
        Color swatchColor,
        string label,
        string maxText,
        string avgText,
        bool darkChrome)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var swatch = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(swatchColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var name = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var values = new TextBlock
        {
            Text = $"max {maxText}  ·  avg {avgText}",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
        };

        Grid.SetRow(swatch, row);
        Grid.SetColumn(swatch, 0);
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 1);
        Grid.SetRow(values, row);
        Grid.SetColumn(values, 2);
        grid.Children.Add(swatch);
        grid.Children.Add(name);
        grid.Children.Add(values);
        row++;
    }

    private static TextBlock DiagnosticTextBlock(string text, bool darkChrome) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.WrapWholeWords,
        Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
    };

    private static StackPanel BuildLegend(bool darkChrome, params (string Label, Color Color, bool Dashed)[] items)
    {
        var labelBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var (label, color, dashed) in items)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (dashed)
            {
                item.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 3,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = new SolidColorBrush(color),
                    Opacity = 0.85,
                });
            }
            else
            {
                item.Children.Add(new Rectangle
                {
                    Width = 12,
                    Height = 3,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = new SolidColorBrush(color),
                });
            }

            item.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = labelBrush,
            });
            row.Children.Add(item);
        }

        return row;
    }

    private static ElementTheme ResolveChromeTheme(FrameworkElement host) =>
        ThemeBridge.ResolveChromeTheme(host);

    private static async Task ShowLargeChartWindowAsync(
        Window ownerWindow,
        string title,
        Panel body,
        ElementTheme chromeTheme,
        Thickness? bodyMargin = null,
        PerformanceChartLiveDetailSession? liveSession = null,
        PerformanceChartDetailChromeCollector? chromeCollector = null)
    {
        body.Margin = bodyMargin ?? DetailBodyMargin;
        var darkChrome = chromeTheme != ElementTheme.Light;

        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96,
            Margin = new Thickness(16, 0, 16, 14),
        };

        var root = new Grid
        {
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtWindowBgBrush"),
            RequestedTheme = chromeTheme,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bodyHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
        Grid.SetRow(bodyHost, 0);
        Grid.SetRow(closeButton, 1);
        root.Children.Add(bodyHost);
        root.Children.Add(closeButton);

        var dialogWindow = new Window { Title = title, Content = root };
        var sandboxChartIcon = App.LaunchSandboxMonitor || ownerWindow is SandboxMonitorWindow;
        AppBrandingIcons.TryApplySessionIcon(dialogWindow, sandboxChartIcon);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        closeButton.Click += (_, _) => dialogWindow.Close();
        dialogWindow.Closed += (_, _) => tcs.TrySetResult();

        try
        {
            TitleBarThemeHelper.Apply(dialogWindow, chromeTheme);
            var (clientW, clientH) = ResolveWindowClientSize(ownerWindow);
            var dialogHwnd = WindowNative.GetWindowHandle(dialogWindow);
            var dialogApp = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(dialogHwnd));
            var scale = (ownerWindow.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            var targetW = (int)Math.Max(800, clientW * scale * 0.94);
            var targetH = (int)Math.Max(560, clientH * scale * 0.90);
            dialogApp.Resize(new SizeInt32(targetW, targetH));

            var ownerHwnd = WindowNative.GetWindowHandle(ownerWindow);
            var ownerApp = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd));
            var ox = ownerApp.Position.X + (ownerApp.Size.Width - targetW) / 2;
            var oy = ownerApp.Position.Y + (ownerApp.Size.Height - targetH) / 2;
            dialogApp.Move(new PointInt32(Math.Max(0, ox), Math.Max(0, oy)));
        }
        catch
        {
            // best-effort sizing
        }

        var chromeCoordinator = chromeCollector?.Bind(dialogWindow, root, ownerWindow, chromeTheme);

        dialogWindow.Activate();
        liveSession?.Start();
        try
        {
            await tcs.Task.ConfigureAwait(true);
        }
        finally
        {
            chromeCoordinator?.Dispose();
            liveSession?.Dispose();
        }
    }

    private static (double Width, double Height) ResolveWindowClientSize(Window ownerWindow)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(ownerWindow);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            var scale = (ownerWindow.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
            return (appWindow.Size.Width / scale, appWindow.Size.Height / scale);
        }
        catch
        {
            return (960, 720);
        }
    }
}
