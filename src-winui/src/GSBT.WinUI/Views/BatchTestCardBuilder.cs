using GSBT.WinUI.Controls;
using GSBT.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GSBT.WinUI.Views;

public sealed class BatchTestCardHost
{
    public int Index { get; init; }
    public string TitleKey { get; init; } = "";
    public string ParamsKey { get; init; } = "";
    /// <summary>2px highlight ring (transparent when idle; segment color or running pink when active).</summary>
    public Border OuterHighlightBorder { get; init; } = null!;
    /// <summary>Inner card with the default 1px border.</summary>
    public Border Card { get; init; } = null!;
    public FrameworkElement Root { get; init; } = null!;
    public TextBlock StatusText { get; init; } = null!;
    public TextBlock TitleText { get; init; } = null!;
    public TextBlock ParametersText { get; init; } = null!;
    public ProgressBar ProgressBar { get; init; } = null!;
    public Brush DefaultBorderBrush { get; set; } = null!;
    public Brush ActiveBorderBrush { get; init; } = null!;
    public bool IsRunningHighlight { get; set; }
    public bool IsSegmentHighlight { get; set; }
    public Color SegmentHighlightColor { get; set; }
    public Button? ZoomToTestButton { get; init; }
    public Button? ResetChartViewButton { get; init; }
}

public readonly record struct BatchTestCardLayoutOptions(
    double MinCardWidth = 200,
    double CardGap = 8,
    int MaxCardColumns = 3,
    double MaxCardWidth = 280,
    bool UseFixedWidthColumns = false);

/// <summary>Shared batch test cards for Performance pane and Combined diagnostics.</summary>
public static class BatchTestCardBuilder
{
    private const double InnerBorderThickness = 1;
    private const double HighlightBorderThickness = 2;
    private const double DetailActionButtonSize = 22;
    private static readonly SolidColorBrush RunningBorderBrush = new(Color.FromArgb(255, 255, 100, 160));
    private static readonly SolidColorBrush TransparentHighlightBrush = new(Color.FromArgb(0, 0, 0, 0));

    public static bool IsPrimaryPointerPressed(PointerRoutedEventArgs e) =>
        e.GetCurrentPoint(null).Properties.IsLeftButtonPressed;

    public static BatchTestCardHost Create(
        BatchTestRunSnapshot snapshot,
        bool darkChrome,
        bool showProgressBar = true,
        bool showDetailChartActions = false)
    {
        var status = new TextBlock
        {
            FontSize = 10,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cardTitle = BatchTestDisplayName.TruncateForCard(snapshot.Title);
        var title = new TextBlock
        {
            Text = cardTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (cardTitle != snapshot.Title)
        {
            ToolTipService.SetToolTip(title, snapshot.Title);
        }

        Grid.SetColumn(title, 0);
        Grid.SetColumn(status, 1);
        header.Children.Add(title);
        header.Children.Add(status);

        var parameters = new TextBlock
        {
            Text = snapshot.ParametersLine,
            FontSize = 10,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        };

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 3,
            Margin = new Thickness(0, 6, 0, 0),
            IsIndeterminate = false,
            Visibility = showProgressBar ? Visibility.Visible : Visibility.Collapsed,
        };

        var inner = new StackPanel { Spacing = 0 };
        inner.Children.Add(header);
        inner.Children.Add(parameters);
        if (showProgressBar)
        {
            inner.Children.Add(progress);
        }

        Button? zoomBtn = null;
        Button? resetBtn = null;
        FrameworkElement cardContent = inner;

        if (showDetailChartActions)
        {
            zoomBtn = CreateDetailActionButton("\uE71E", "Zoom chart to this test");
            resetBtn = CreateDetailActionButton("\uE72C", "Reset chart zoom and scroll to full run");
            var actions = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    zoomBtn,
                    resetBtn,
                },
            };

            var body = new Grid { ColumnSpacing = 6 };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(inner, 0);
            Grid.SetColumn(actions, 1);
            body.Children.Add(inner);
            body.Children.Add(actions);
            cardContent = body;
        }

        var card = new Border
        {
            MinWidth = 200,
            BorderThickness = new Thickness(InnerBorderThickness),
            BorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush"),
            Child = cardContent,
        };

        var outer = new Border
        {
            BorderThickness = new Thickness(HighlightBorderThickness),
            BorderBrush = TransparentHighlightBrush,
            CornerRadius = new CornerRadius(8),
            Child = card,
        };

        var root = outer;

        var host = new BatchTestCardHost
        {
            Index = snapshot.Index,
            TitleKey = snapshot.Title,
            ParamsKey = snapshot.ParametersLine,
            OuterHighlightBorder = outer,
            Root = root,
            Card = card,
            StatusText = status,
            ProgressBar = progress,
            TitleText = title,
            ParametersText = parameters,
            DefaultBorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush"),
            ActiveBorderBrush = RunningBorderBrush,
            ZoomToTestButton = zoomBtn,
            ResetChartViewButton = resetBtn,
        };

        ApplyRunningState(host, snapshot, isActive: false);
        return host;
    }

    private static Button CreateDetailActionButton(string glyph, string toolTip)
    {
        var btn = new Button
        {
            Width = DetailActionButtonSize,
            Height = DetailActionButtonSize,
            MinWidth = DetailActionButtonSize,
            MinHeight = DetailActionButtonSize,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 11 },
        };
        ToolTipService.SetToolTip(btn, toolTip);
        return btn;
    }

    public static void ApplyChromeTheme(BatchTestCardHost host, bool darkChrome)
    {
        host.DefaultBorderBrush = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBorderBrush");
        host.Card.Background = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtCardBgBrush");
        host.Card.BorderBrush = host.DefaultBorderBrush;
        host.TitleText.Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtBodyTextBrush");
        host.StatusText.Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");
        host.ParametersText.Foreground = ThemeBridge.GetGsbtBrush(darkChrome, "GsbtSecondaryLabelBrush");
        ApplyHighlightBorder(host);
    }

    public static void ApplyRunningState(BatchTestCardHost host, BatchTestRunSnapshot snapshot, bool isActive)
    {
        host.StatusText.Text = snapshot.Phase switch
        {
            BatchTestRunPhase.Running => "Running",
            BatchTestRunPhase.Completed => "Done",
            BatchTestRunPhase.Failed => "Failed",
            BatchTestRunPhase.Cancelled => "Cancelled",
            _ => "Queued",
        };

        host.IsRunningHighlight = isActive;
        host.Card.BorderBrush = host.DefaultBorderBrush;
        host.Card.BorderThickness = new Thickness(InnerBorderThickness);
        host.OuterHighlightBorder.Opacity = 1;
        ApplyHighlightBorder(host);

        var cardTitle = BatchTestDisplayName.TruncateForCard(snapshot.Title);
        host.TitleText.Text = cardTitle;
        if (cardTitle != snapshot.Title)
        {
            ToolTipService.SetToolTip(host.TitleText, snapshot.Title);
        }
        else
        {
            ToolTipService.SetToolTip(host.TitleText, null);
        }

        var showProgress = snapshot.Phase is BatchTestRunPhase.Running or BatchTestRunPhase.Completed;
        host.ProgressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        if (showProgress)
        {
            var target = snapshot.Phase == BatchTestRunPhase.Completed ? 100 : snapshot.ProgressPercent;
            if (Math.Abs(host.ProgressBar.Value - target) > 0.01)
            {
                host.ProgressBar.Value = target;
            }
        }
    }

    public static void ApplySegmentSelection(BatchTestCardHost host, bool selected, Color segmentColor)
    {
        host.IsSegmentHighlight = selected;
        host.SegmentHighlightColor = segmentColor;
        host.Card.BorderBrush = host.DefaultBorderBrush;
        host.Card.BorderThickness = new Thickness(InnerBorderThickness);
        host.OuterHighlightBorder.Opacity = 1;
        ApplyHighlightBorder(host);
    }

    public static void ApplyDisabled(BatchTestCardHost host, bool disabled)
    {
        host.Root.IsHitTestVisible = !disabled;
        host.Root.Opacity = disabled ? 0.4 : 1;
    }

    private static void ApplyHighlightBorder(BatchTestCardHost host)
    {
        if (host.IsRunningHighlight)
        {
            host.OuterHighlightBorder.BorderBrush = host.ActiveBorderBrush;
            return;
        }

        if (host.IsSegmentHighlight)
        {
            host.OuterHighlightBorder.BorderBrush = new SolidColorBrush(host.SegmentHighlightColor);
            return;
        }

        host.OuterHighlightBorder.BorderBrush = TransparentHighlightBrush;
    }

    public static void RelayoutCards(
        Grid host,
        IReadOnlyList<BatchTestCardHost> cards,
        BatchTestCardLayoutOptions? options = null)
    {
        var opts = options ?? new BatchTestCardLayoutOptions();
        if (cards.Count == 0)
        {
            host.Children.Clear();
            host.RowDefinitions.Clear();
            host.ColumnDefinitions.Clear();
            return;
        }

        var available = host.ActualWidth;
        if (available <= 0)
        {
            available = 680;
        }

        int cols;
        double columnWidth;
        if (opts.UseFixedWidthColumns)
        {
            columnWidth = opts.MaxCardWidth;
            var maxCols = Math.Min(cards.Count, opts.MaxCardColumns);
            cols = Math.Clamp((int)((available + opts.CardGap) / (columnWidth + opts.CardGap)), 1, maxCols);
            if (cols == 1 && available < columnWidth)
            {
                columnWidth = Math.Clamp(available, opts.MinCardWidth, opts.MaxCardWidth);
            }
        }
        else
        {
            cols = Math.Clamp((int)((available + opts.CardGap) / (opts.MinCardWidth + opts.CardGap)), 1, opts.MaxCardColumns);
            columnWidth = 0;
        }

        var rows = (int)Math.Ceiling(cards.Count / (double)cols);

        host.Children.Clear();
        host.RowDefinitions.Clear();
        host.ColumnDefinitions.Clear();

        for (var r = 0; r < rows; r++)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var c = 0; c < cols; c++)
        {
            host.ColumnDefinitions.Add(opts.UseFixedWidthColumns
                ? new ColumnDefinition { Width = new GridLength(columnWidth, GridUnitType.Pixel) }
                : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i].Root;
            Grid.SetRow(card, i / cols);
            Grid.SetColumn(card, i % cols);
            card.Margin = new Thickness(
                i % cols == 0 ? 0 : opts.CardGap / 2,
                i < cols ? 0 : opts.CardGap,
                i % cols == cols - 1 ? 0 : opts.CardGap / 2,
                0);
            host.Children.Add(card);
        }
    }
}
