using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using GSBT.WinUI;
using GSBT.WinUI.Services;
using GSBT.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace GSBT.WinUI.Controls;

public sealed partial class GameTableView : UserControl
{
    /// <summary>Per-row fade length after header sort (ms). Tune in GameTableView.xaml.cs.</summary>
    private const double SortFadeRowDurationMs = 100;

    /// <summary>Max delay between first and last visible row (ms). Tune in GameTableView.xaml.cs.</summary>
    private const double SortFadeMaxSpreadMs = 180;

    /// <summary>Starting opacity for the cascade fade (0–1). Tune in GameTableView.xaml.cs.</summary>
    private const double SortFadeStartOpacity = 0.45;

    private static readonly Brush LastBackupIntegrityWarningForeground =
        new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 102, 102));

    private static readonly Brush LastBackupCheckpointWarningForeground =
        new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 80));

    /// <summary>Right-click (or context key) on a row — host builds a <see cref="MenuFlyout"/>.</summary>
    public event EventHandler<GameRowContextRequestedEventArgs>? RowContextRequested;

    /// <summary>Matches <see cref="GameTableView"/> row template <c>Height</c> for scroll-based hit testing.</summary>
    private const double RowHeightPx = 28;

    private IReadOnlyList<GameTableColumn> _visibleDefs = GameTableColumns.Definitions;
    private readonly Dictionary<string, double> _fixedWidthByColumnId = new(StringComparer.Ordinal);

    private PointerEventHandler? _tablePtrPressed;
    private PointerEventHandler? _tablePtrMoved;
    private PointerEventHandler? _tablePtrReleased;
    private PointerEventHandler? _tablePtrCanceled;

    /// <summary>Pointer down on list viewport but not on a row — click clears selection.</summary>
    private bool _listEmptyAreaCapture;

    private uint _listPointerId;

    private ScrollViewer? _listScrollViewer;

    /// <summary>Click-drag on a row extends selection from anchor through the row under the cursor (not rectangle marquee).</summary>
    private bool _rowDragActive;
    private int _rowDragAnchorIndex;
    private bool _rowDragAdditive;

    /// <summary>Avoids re-applying the same range every pointer move (reduces jitter).</summary>
    private int _lastAppliedDragHitIndex = -1;

    private ObservableCollection<GameRowViewModel>? _wiredObservableGames;
    private readonly Dictionary<GameRowViewModel, PropertyChangedEventHandler> _rowPropertyHandlers = new();

    public GameTableView()
    {
        InitializeComponent();
        RebuildColumnModel();
        Loaded += GameTableView_Loaded;
        Unloaded += GameTableView_Unloaded;
    }

    /// <summary>Re-read column visibility from settings and rebuild header + row grids.</summary>
    public void RefreshColumnLayout()
    {
        RebuildColumnModel();
        BuildHeader();
        foreach (var item in GamesGrid.Items)
        {
            if (GamesGrid.ContainerFromItem(item) is ListViewItem li && li.ContentTemplateRoot is Grid rowGrid)
            {
                rowGrid.Tag = null;
                rowGrid.ColumnDefinitions.Clear();
                rowGrid.Children.Clear();
                if (item is GameRowViewModel vm)
                {
                    EnsureRowStructure(rowGrid);
                    ApplyRowTexts(rowGrid, vm);
                }
            }
        }

        ApplyAllColumnWidths();
    }

    private static SettingsStore? TryGetSettingsStore()
    {
        try
        {
            return App.Host?.Services.GetService<SettingsStore>();
        }
        catch
        {
            return null;
        }
    }

    private void RebuildColumnModel()
    {
        _visibleDefs = GameTableColumnVisibility.FilterVisibleColumns(
            GameTableColumns.Definitions,
            TryGetSettingsStore());

        if (_visibleDefs.Count(c => c.IsStarColumn) != 1)
        {
            throw new InvalidOperationException("Visible game table columns must contain exactly one star column.");
        }

        _fixedWidthByColumnId.Clear();
        foreach (var d in _visibleDefs)
        {
            if (!d.IsStarColumn)
            {
                _fixedWidthByColumnId[d.Id] = d.InitialPixelWidth;
            }
        }
    }

    /// <summary>Exposes the inner list for selection, shortcuts, and double-click handlers outside this control.</summary>
    public ListView RowsListView => GamesGrid;

    /// <summary>Quick top-to-bottom opacity cascade after the list reorders on header sort.</summary>
    public async Task PlaySortCascadeFadeAsync()
    {
        var count = GamesGrid.Items.Count;
        if (count == 0)
        {
            return;
        }

        // Let ListView lay out containers after the collection reorder.
        await Task.Yield();

        var staggerMs = count <= 1 ? 0.0 : SortFadeMaxSpreadMs / (count - 1);
        var animations = new List<Task>();

        for (var i = 0; i < count; i++)
        {
            if (GamesGrid.ContainerFromItem(GamesGrid.Items[i]) is not ListViewItem row)
            {
                continue;
            }

            row.Opacity = SortFadeStartOpacity;
            var delayMs = (int)Math.Round(i * staggerMs);
            animations.Add(AnimateListRowOpacityAsync(row, SortFadeStartOpacity, 1.0, SortFadeRowDurationMs, delayMs));
        }

        if (animations.Count == 0)
        {
            return;
        }

        await Task.WhenAll(animations);
    }

    private static async Task AnimateListRowOpacityAsync(
        ListViewItem row,
        double from,
        double to,
        double durationMs,
        int delayMs)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
        }

        var tcs = new TaskCompletionSource<bool>();
        var storyboard = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(anim, row);
        Storyboard.SetTargetProperty(anim, "Opacity");
        storyboard.Children.Add(anim);
        storyboard.Completed += (_, _) => tcs.TrySetResult(true);
        storyboard.Begin();
        await tcs.Task;
    }

    public object? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(object),
            typeof(GameTableView),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public string? SortColumnId
    {
        get => (string?)GetValue(SortColumnIdProperty);
        set => SetValue(SortColumnIdProperty, value);
    }

    public static readonly DependencyProperty SortColumnIdProperty =
        DependencyProperty.Register(
            nameof(SortColumnId),
            typeof(string),
            typeof(GameTableView),
            new PropertyMetadata(null, OnSortDisplayChanged));

    public bool SortAscending
    {
        get => (bool)GetValue(SortAscendingProperty);
        set => SetValue(SortAscendingProperty, value);
    }

    public static readonly DependencyProperty SortAscendingProperty =
        DependencyProperty.Register(
            nameof(SortAscending),
            typeof(bool),
            typeof(GameTableView),
            new PropertyMetadata(true, OnSortDisplayChanged));

    /// <summary>User clicked a column title to sort.</summary>
    public event Action<string>? HeaderSortRequested;

    private static void OnSortDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GameTableView v)
        {
            v.BuildHeader();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GameTableView v)
        {
            return;
        }

        v.RewireItemPropertyNotifications(e.OldValue, e.NewValue);
        v.GamesGrid.ItemsSource = e.NewValue as IEnumerable;
        v.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            v.PatchListViewScrollAreaHitTestable);
    }

    private void RewireItemPropertyNotifications(object? oldValue, object? newValue)
    {
        if (_wiredObservableGames is not null)
        {
            _wiredObservableGames.CollectionChanged -= GamesSource_CollectionChanged;
            foreach (var vm in _wiredObservableGames)
            {
                UnsubscribeRow(vm);
            }

            _wiredObservableGames = null;
        }
        else if (oldValue is IEnumerable oldEn)
        {
            foreach (var vm in oldEn.OfType<GameRowViewModel>())
            {
                UnsubscribeRow(vm);
            }
        }

        if (newValue is ObservableCollection<GameRowViewModel> obs)
        {
            _wiredObservableGames = obs;
            obs.CollectionChanged += GamesSource_CollectionChanged;
            foreach (var vm in obs)
            {
                SubscribeRow(vm);
            }
        }
        else if (newValue is IEnumerable newEn)
        {
            foreach (var vm in newEn.OfType<GameRowViewModel>())
            {
                SubscribeRow(vm);
            }
        }
    }

    private void GamesSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var o in e.OldItems)
            {
                if (o is GameRowViewModel oldVm)
                {
                    UnsubscribeRow(oldVm);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var o in e.NewItems)
            {
                if (o is GameRowViewModel newVm)
                {
                    SubscribeRow(newVm);
                }
            }
        }
    }

    private void SubscribeRow(GameRowViewModel vm)
    {
        if (_rowPropertyHandlers.ContainsKey(vm))
        {
            return;
        }

        void Handler(object? s, PropertyChangedEventArgs args) => OnRowViewModelPropertyChanged(vm);
        vm.PropertyChanged += Handler;
        _rowPropertyHandlers[vm] = Handler;
    }

    private void UnsubscribeRow(GameRowViewModel vm)
    {
        if (_rowPropertyHandlers.TryGetValue(vm, out var h))
        {
            vm.PropertyChanged -= h;
            _rowPropertyHandlers.Remove(vm);
        }
    }

    private void OnRowViewModelPropertyChanged(GameRowViewModel vm)
    {
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
            () => RefreshRowVisual(vm));
    }

    private void RefreshRowVisual(GameRowViewModel vm)
    {
        if (GamesGrid.ContainerFromItem(vm) is not ListViewItem li || li.ContentTemplateRoot is not Grid rowGrid)
        {
            return;
        }

        EnsureRowStructure(rowGrid);
        ApplyRowTexts(rowGrid, vm);
    }

    private void GameTableView_Loaded(object sender, RoutedEventArgs e)
    {
        BuildHeader();
        ActualThemeChanged += GameTableView_ActualThemeChanged;
        GamesGrid.Loaded += GamesGrid_LoadedPatchScrollHitTest;
        _tablePtrPressed = new PointerEventHandler(Table_PointerPressed);
        _tablePtrMoved = new PointerEventHandler(Table_PointerMoved);
        _tablePtrReleased = new PointerEventHandler(Table_PointerReleased);
        _tablePtrCanceled = new PointerEventHandler(Table_PointerCanceled);
        // ListHost + handledEventsToo: ListView often marks empty-area presses handled before they bubble to the root.
        ListHost.AddHandler(PointerPressedEvent, _tablePtrPressed, true);
        ListHost.AddHandler(PointerMovedEvent, _tablePtrMoved, true);
        ListHost.AddHandler(PointerReleasedEvent, _tablePtrReleased, true);
        ListHost.AddHandler(PointerCanceledEvent, _tablePtrCanceled, true);
        GamesGrid.KeyDown += GamesGrid_KeyDown;
        PatchListViewScrollAreaHitTestable();
        ApplyAllColumnWidths();
        ToolTipService.SetToolTip(GamesGrid, null);
    }

    private void GamesGrid_LoadedPatchScrollHitTest(object sender, RoutedEventArgs e)
    {
        PatchListViewScrollAreaHitTestable();
        _listScrollViewer ??= FindFirstScrollViewer(GamesGrid);
    }

    private void GameTableView_Unloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= GameTableView_ActualThemeChanged;
        GamesGrid.Loaded -= GamesGrid_LoadedPatchScrollHitTest;
        if (_tablePtrPressed is not null)
        {
            ListHost.RemoveHandler(PointerPressedEvent, _tablePtrPressed);
        }

        if (_tablePtrMoved is not null)
        {
            ListHost.RemoveHandler(PointerMovedEvent, _tablePtrMoved);
        }

        if (_tablePtrReleased is not null)
        {
            ListHost.RemoveHandler(PointerReleasedEvent, _tablePtrReleased);
        }

        if (_tablePtrCanceled is not null)
        {
            ListHost.RemoveHandler(PointerCanceledEvent, _tablePtrCanceled);
        }

        GamesGrid.KeyDown -= GamesGrid_KeyDown;
        RewireItemPropertyNotifications(ItemsSource, null);
    }

    private void GameTableView_ActualThemeChanged(FrameworkElement sender, object args)
    {
        RefreshThemeVisuals();
    }

    public void RefreshThemeVisuals()
    {
        RefreshThemeVisualsCore();
        // ListView containers may not exist while settings overlay hides the grid; second pass picks up rows after layout.
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, RefreshThemeVisualsCore);
    }

    private void RefreshThemeVisualsCore()
    {
        BuildHeader();
        ApplyAllColumnWidths();
        RefreshAllRowTextBrushes();
    }

    private void RefreshAllRowTextBrushes()
    {
        foreach (var item in GamesGrid.Items)
        {
            if (item is not GameRowViewModel vm)
            {
                continue;
            }

            var container = GamesGrid.ContainerFromItem(item);
            if (container is ListViewItem listItem && listItem.ContentTemplateRoot is Grid rowGrid)
            {
                ApplyRowTexts(rowGrid, vm);
            }
        }
    }

    private static bool InAppBackupWarningsEnabled()
    {
        try
        {
            var store = App.Host?.Services.GetService<SettingsStore>();
            return store?.Get("in_app_backup_warnings_enabled", true) ?? true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// ListView's inner ScrollViewer / ScrollContentPresenter often have no Background, so pointer events
    /// miss the viewport below the last row — rubber-band and click-to-clear never fire. Force transparent fills.
    /// </summary>
    private void PatchListViewScrollAreaHitTestable()
    {
        try
        {
            var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            PatchScrollViewportHitTestWalk(GamesGrid, transparent);
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>Only scroll chrome — avoid touching ListViewItem or other controls (would break row visuals).</summary>
    private static void PatchScrollViewportHitTestWalk(DependencyObject node, Brush transparent)
    {
        switch (node)
        {
            case ScrollViewer sv when sv.Background is null:
                sv.Background = transparent;
                break;
            case ScrollContentPresenter scp when scp.Background is null:
                scp.Background = transparent;
                break;
        }

        var n = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < n; i++)
        {
            PatchScrollViewportHitTestWalk(VisualTreeHelper.GetChild(node, i), transparent);
        }
    }

    /// <summary>Header row: game name (star) plus fixed-width columns; no user column resize.</summary>
    private void BuildHeader()
    {
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();

        FillColumnDefinitions(HeaderGrid);

        for (var col = 0; col < _visibleDefs.Count; col++)
        {
            var def = _visibleDefs[col];
            var cell = CreateHeaderCell(def, col);
            HeaderGrid.Children.Add(cell);
        }
    }

    private Border CreateHeaderCell(GameTableColumn def, int col)
    {
        var cell = new Border
        {
            Style = (Style)Resources["GsbtTableHeaderCellBorderStyle"],
            BorderThickness = new Thickness(0),
            Padding = col == 0 ? new Thickness(10, 0, 8, 0) : new Thickness(8, 0, 8, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Tag = def.Id,
        };
        Grid.SetColumn(cell, col);
        cell.PointerPressed += HeaderCell_PointerPressed;
        cell.Child = CreateHeaderLabelContent(def, col);
        return cell;
    }

    private UIElement CreateHeaderLabelContent(GameTableColumn def, int col)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
            IsHitTestVisible = false,
            Margin = col == 0 ? new Thickness(10, 0, 0, 0) : new Thickness(0),
        };

        panel.Children.Add(new TextBlock
        {
            Text = def.Header,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Resources["GsbtTableHeaderTextStyle"],
        });

        if (string.Equals(SortColumnId, def.Id, StringComparison.OrdinalIgnoreCase))
        {
            // Glyph choice: \u25B4/\u25BE (small), \u25B2/\u25BC (filled, reads larger at same FontSize).
            panel.Children.Add(new TextBlock
            {
                Text = SortAscending ? "\u25B4" : "\u25BE",
                Style = (Style)Resources["GsbtTableHeaderSortIndicatorStyle"],
            });
        }

        return panel;
    }

    private void HeaderCell_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border { Tag: string columnId })
        {
            return;
        }

        HeaderSortRequested?.Invoke(columnId);
        e.Handled = true;
    }

    private void FillColumnDefinitions(Grid grid)
    {
        grid.ColumnDefinitions.Clear();
        foreach (var def in _visibleDefs)
        {
            if (def.IsStarColumn)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = def.MinPixelWidth
                });
            }
            else
            {
                var fixedWidth = _fixedWidthByColumnId[def.Id];
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fixedWidth), MinWidth = 0 });
            }
        }
    }

    private void SyncColumnWidths(Grid grid)
    {
        if (grid.ColumnDefinitions.Count != _visibleDefs.Count)
        {
            return;
        }

        for (var i = 0; i < _visibleDefs.Count; i++)
        {
            var def = _visibleDefs[i];
            var cd = grid.ColumnDefinitions[i];
            if (def.IsStarColumn)
            {
                cd.Width = new GridLength(1, GridUnitType.Star);
                cd.MinWidth = def.MinPixelWidth;
            }
            else
            {
                var fixedWidth = _fixedWidthByColumnId[def.Id];
                cd.Width = new GridLength(fixedWidth);
                cd.MinWidth = 0;
            }
        }
    }

    /// <summary>Propagates <see cref="_fixedWidths"/> to the header and every realized row grid.</summary>
    private void ApplyAllColumnWidths()
    {
        if (HeaderGrid.ColumnDefinitions.Count == _visibleDefs.Count)
        {
            SyncColumnWidths(HeaderGrid);
        }
        else
        {
            BuildHeader();
        }

        foreach (var item in GamesGrid.Items)
        {
            var container = GamesGrid.ContainerFromItem(item);
            if (container is not ListViewItem listItem || listItem.ContentTemplateRoot is not Grid rowGrid)
            {
                continue;
            }

            if (rowGrid.ColumnDefinitions.Count == _visibleDefs.Count)
            {
                SyncColumnWidths(rowGrid);
            }
            else if (listItem.DataContext is GameRowViewModel vm)
            {
                EnsureRowStructure(rowGrid);
                ApplyRowTexts(rowGrid, vm);
            }
        }
    }

    private void GamesGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not GameRowViewModel vm)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            return;
        }

        args.RegisterUpdateCallback((_, a) =>
        {
            a.ItemContainer.Opacity = 1.0;

            if (a.ItemContainer.ContentTemplateRoot is not Grid rowGrid)
            {
                return;
            }

            EnsureRowStructure(rowGrid);
            ApplyRowTexts(rowGrid, vm);
        });
    }

    private sealed record RowCells(TextBlock[] Texts);

    private void EnsureRowStructure(Grid rowGrid)
    {
        if (rowGrid.Tag is RowCells rc
            && rc.Texts.Length == _visibleDefs.Count
            && rowGrid.ColumnDefinitions.Count == _visibleDefs.Count)
        {
            SyncColumnWidths(rowGrid);
            return;
        }

        rowGrid.ColumnDefinitions.Clear();
        rowGrid.Children.Clear();
        FillColumnDefinitions(rowGrid);

        var cellBorderStyle = (Style)Resources["GsbtTableRowCellBorderStyle"];
        var bodyTextStyle = (Style)Resources["GsbtTableCellBodyTextStyle"];

        var texts = new TextBlock[_visibleDefs.Count];
        for (var col = 0; col < _visibleDefs.Count; col++)
        {
            var cell = new Border
            {
                Style = cellBorderStyle,
                BorderThickness = col > 0 ? new Thickness(1, 0, 0, 0) : new Thickness(0),
                Padding = new Thickness(10, 0, 8, 0)
            };
            Grid.SetColumn(cell, col);
            var tb = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Style = bodyTextStyle
            };
            cell.Child = tb;
            texts[col] = tb;
            rowGrid.Children.Add(cell);
        }

        rowGrid.Tag = new RowCells(texts);
        rowGrid.RightTapped -= RowGrid_RightTapped;
        rowGrid.RightTapped += RowGrid_RightTapped;
    }

    private void RowGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not GameRowViewModel vm)
        {
            return;
        }

        e.Handled = true;
        var pos = e.GetPosition(fe);
        RowContextRequested?.Invoke(this, new GameRowContextRequestedEventArgs(vm, fe, pos));
    }

    private void GamesGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.F10)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (!shift.HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }

        if (GamesGrid.SelectedItem is not GameRowViewModel vm)
        {
            return;
        }

        if (GamesGrid.ContainerFromItem(vm) is not ListViewItem li || li.ContentTemplateRoot is not Grid rowGrid)
        {
            return;
        }

        e.Handled = true;
        var pos = new Point(rowGrid.ActualWidth * 0.5, rowGrid.ActualHeight * 0.5);
        RowContextRequested?.Invoke(this, new GameRowContextRequestedEventArgs(vm, rowGrid, pos));
    }

    private void ApplyRowTexts(Grid rowGrid, GameRowViewModel vm)
    {
        if (rowGrid.Tag is not RowCells rc)
        {
            return;
        }

        for (var i = 0; i < _visibleDefs.Count; i++)
        {
            rc.Texts[i].Text = _visibleDefs[i].GetText(vm);
            var columnId = _visibleDefs[i].Id;
            var inAppOn = InAppBackupWarningsEnabled();
            var redLastBackup = inAppOn
                && vm.LastBackupIntegrityWarning
                && string.Equals(columnId, "lastBackup", StringComparison.Ordinal)
                && string.Equals(vm.LastBackup, "Not yet backed up", StringComparison.Ordinal);
            var yellowLastBackup = inAppOn
                && !redLastBackup
                && vm.LastBackupCheckpointWarning
                && string.Equals(columnId, "lastBackup", StringComparison.Ordinal);
            if (redLastBackup)
            {
                rc.Texts[i].Foreground = LastBackupIntegrityWarningForeground;
            }
            else if (yellowLastBackup)
            {
                rc.Texts[i].Foreground = LastBackupCheckpointWarningForeground;
            }
            else
            {
                rc.Texts[i].ClearValue(TextBlock.ForegroundProperty);
            }
        }
    }

    private void Table_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsScrollbarPart(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (!IsDescendantOf(e.OriginalSource as DependencyObject, GamesGrid))
        {
            return;
        }

        var pt = e.GetCurrentPoint(ListHost);
        if (pt.Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed)
        {
            return;
        }

        var listItem = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (listItem != null)
        {
            var itemObj = GamesGrid.ItemFromContainer(listItem);
            if (itemObj is not GameRowViewModel vm)
            {
                return;
            }

            var idx = GamesGrid.Items.IndexOf(itemObj);
            if (idx < 0)
            {
                return;
            }

            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            var additive = ctrl.HasFlag(CoreVirtualKeyStates.Down);

            _rowDragActive = true;
            _listPointerId = e.Pointer.PointerId;
            _rowDragAnchorIndex = idx;
            _rowDragAdditive = additive;
            _lastAppliedDragHitIndex = idx;

            if (!additive)
            {
                GamesGrid.SelectedItems.Clear();
                GamesGrid.SelectedItems.Add(vm);
            }
            else if (!GamesGrid.SelectedItems.Contains(vm))
            {
                GamesGrid.SelectedItems.Add(vm);
            }

            ListHost.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _listEmptyAreaCapture = true;
        _listPointerId = e.Pointer.PointerId;
        ListHost.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Table_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_rowDragActive || e.Pointer.PointerId != _listPointerId)
        {
            return;
        }

        var pos = e.GetCurrentPoint(ListHost).Position;
        var hitIdx = HitTestRowIndex(pos);
        if (hitIdx < 0)
        {
            return;
        }

        if (hitIdx == _lastAppliedDragHitIndex)
        {
            return;
        }

        _lastAppliedDragHitIndex = hitIdx;
        ApplyRowRangeSelection(_rowDragAnchorIndex, hitIdx, _rowDragAdditive);
        e.Handled = true;
    }

    private void Table_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rowDragActive && e.Pointer.PointerId == _listPointerId)
        {
            ListHost.ReleasePointerCapture(e.Pointer);
            _rowDragActive = false;
            _lastAppliedDragHitIndex = -1;
            e.Handled = true;
            return;
        }

        if (!_listEmptyAreaCapture || e.Pointer.PointerId != _listPointerId)
        {
            return;
        }

        ListHost.ReleasePointerCapture(e.Pointer);
        _listEmptyAreaCapture = false;

        GamesGrid.SelectedItems.Clear();
        e.Handled = true;
    }

    private void Table_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _listPointerId)
        {
            return;
        }

        ListHost.ReleasePointerCapture(e.Pointer);
        _listEmptyAreaCapture = false;
        _rowDragActive = false;
        _lastAppliedDragHitIndex = -1;
    }

    /// <summary>Selects every row from <paramref name="anchorIndex"/> through <paramref name="currentIndex"/> (inclusive).</summary>
    private void ApplyRowRangeSelection(int anchorIndex, int currentIndex, bool additive)
    {
        var lo = Math.Min(anchorIndex, currentIndex);
        var hi = Math.Max(anchorIndex, currentIndex);
        if (!additive)
        {
            GamesGrid.SelectedItems.Clear();
            for (var i = lo; i <= hi && i < GamesGrid.Items.Count; i++)
            {
                if (GamesGrid.Items[i] is GameRowViewModel vm)
                {
                    GamesGrid.SelectedItems.Add(vm);
                }
            }

            return;
        }

        for (var i = lo; i <= hi && i < GamesGrid.Items.Count; i++)
        {
            if (GamesGrid.Items[i] is GameRowViewModel vm && !GamesGrid.SelectedItems.Contains(vm))
            {
                GamesGrid.SelectedItems.Add(vm);
            }
        }
    }

    /// <summary>Resolves row index under the pointer; uses realized containers first, then scroll offset + uniform row height.</summary>
    private int HitTestRowIndex(Point listHostPoint)
    {
        foreach (var item in GamesGrid.Items)
        {
            if (GamesGrid.ContainerFromItem(item) is not ListViewItem li)
            {
                continue;
            }

            var bounds = BoundsRelativeTo(li, ListHost);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            if (listHostPoint.X >= bounds.X && listHostPoint.X <= bounds.X + bounds.Width
                && listHostPoint.Y >= bounds.Y && listHostPoint.Y <= bounds.Y + bounds.Height)
            {
                return GamesGrid.Items.IndexOf(item);
            }
        }

        if (_listScrollViewer is not null && GamesGrid.Items.Count > 0)
        {
            var yInContent = listHostPoint.Y + _listScrollViewer.VerticalOffset;
            var idx = (int)Math.Floor(yInContent / RowHeightPx);
            return Math.Clamp(idx, 0, GamesGrid.Items.Count - 1);
        }

        return -1;
    }

    private static ScrollViewer? FindFirstScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
        {
            return sv;
        }

        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var found = FindFirstScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private Rect BoundsRelativeTo(FrameworkElement el, UIElement to)
    {
        try
        {
            var t = el.TransformToVisual(to);
            var size = new Size(el.ActualWidth, el.ActualHeight);
            var p = t.TransformPoint(new Point(0, 0));
            return new Rect(p.X, p.Y, size.Width, size.Height);
        }
        catch
        {
            return default;
        }
    }

    private static bool IsScrollbarPart(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ScrollBar)
            {
                return true;
            }

            d = VisualTreeHelper.GetParent(d);
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t)
            {
                return t;
            }

            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }
}
