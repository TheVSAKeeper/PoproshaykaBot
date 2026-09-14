using KeepShell.Bootstrap;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views;

public partial class DashboardView : UserControl, IView<DashboardViewModel>
{
    private const double StackedWidthThreshold = 720;
    private const double StarBandMinWidth = 320;
    private const double StarBandMinHeight = 96;
    private const double SplitterThickness = 6;
    private const double DragThreshold = 6;
    private const double SwapZone = 0.3;
    private const double ShareStep = 0.05;
    private const double RoomTolerance = 0.5;
    private const string ResizeRefused = "Размер этой плитки сейчас не изменить.";

    private readonly Dictionary<DashboardTileViewModel, ContentControl> _hosts = [];
    private readonly Dictionary<string, int[]> _leafPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<SplitPaneLayout, Grid> _splitGrids = new(ReferenceEqualityComparer.Instance);

    private DashboardViewModel? _viewModel;
    private DashboardTileViewModel? _dragTile;
    private Border? _dragShield;
    private Border? _dropHint;
    private TextBlock? _shareHint;
    private Point _dragOrigin;
    private bool _dragging;
    private bool _stacked;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void ApplyTracks<T>(IList<T> definitions, int count, Func<T> create, Action<T, int> apply)
    {
        while (definitions.Count > count)
        {
            definitions.RemoveAt(definitions.Count - 1);
        }

        while (definitions.Count < count)
        {
            definitions.Add(create());
        }

        for (var i = 0; i < count; i++)
        {
            apply(definitions[i], i);
        }
    }

    private static bool ShouldStack(double width)
    {
        return width > 0 && width < StackedWidthThreshold * FontScaleManager.Current;
    }

    private static bool ShouldStack(double width, PaneLayout pane)
    {
        return width > 0 && width < Math.Max(StackedWidthThreshold * FontScaleManager.Current, pane.MinWidth(StarBandMinWidth));
    }

    private static void CollectLeaves(PaneLayout pane, List<TilePaneLayout> leaves)
    {
        switch (pane)
        {
            case TilePaneLayout leaf:
                leaves.Add(leaf);
                return;

            case SplitPaneLayout split:
                foreach (var child in split.Children)
                {
                    CollectLeaves(child.Pane, leaves);
                }

                return;
        }
    }

    private static double Floor(TrackSize track, double minimum)
    {
        return Floor(track.Length, track, minimum);
    }

    public static double TrackFloor(PaneLayoutSlot slot, bool alongColumns)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return alongColumns
            ? Floor(Track(slot, static target => target.Width), slot.Pane.Width, LeafMinimum(slot.Pane, StarBandMinWidth))
            : Floor(Track(slot, static target => target.Height), slot.Pane.Height, LeafMinimum(slot.Pane, StarBandMinHeight));
    }

    private static double LeafMinimum(PaneLayout pane, double minimum)
    {
        return pane is EmptyPaneLayout ? 0 : minimum;
    }

    private static double Floor(GridLength length, TrackSize track, double minimum)
    {
        return Math.Min(Math.Max(track.Min, length.IsStar ? minimum : 0), track.Max);
    }

    private static GridLength Track(PaneLayoutSlot slot, Func<PaneLayout, TrackSize> axis)
    {
        return slot.HasWeight || axis(slot.Pane).Length.IsStar
            ? new(slot.Weight, GridUnitType.Star)
            : GridLength.Auto;
    }

    private static bool Scrollable(TileBand band)
    {
        return band.Rows.All(row => row.Length.IsAuto) && band.Columns.All(column => column.Length.IsAuto);
    }

    private static FrameworkElement Wrap(FrameworkElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private static double[] Shares(Grid grid, bool alongColumns)
    {
        var sizes = alongColumns
            ? grid.ColumnDefinitions.Select(definition => definition.ActualWidth).ToArray()
            : grid.RowDefinitions.Select(definition => definition.ActualHeight).ToArray();

        var total = sizes.Sum();

        return total <= 0 ? sizes : sizes.Select(size => size / total).ToArray();
    }

    private static PaneSide Side(Point position, Rect bounds)
    {
        var horizontal = (position.X - bounds.X) / Math.Max(bounds.Width, 1);
        var vertical = (position.Y - bounds.Y) / Math.Max(bounds.Height, 1);

        var edges = new (PaneSide Side, double Distance)[]
        {
            (PaneSide.Left, horizontal),
            (PaneSide.Right, 1 - horizontal),
            (PaneSide.Top, vertical),
            (PaneSide.Bottom, 1 - vertical),
        };

        return edges.All(edge => edge.Distance > SwapZone)
            ? PaneSide.None
            : edges.MinBy(edge => edge.Distance).Side;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged -= OnLayoutChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as DashboardViewModel;

        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged += OnLayoutChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RebuildGrid();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(DashboardViewModel.EditNotice), StringComparison.Ordinal)
            || string.IsNullOrEmpty(_viewModel?.EditNotice))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(AnnounceNotice));
    }

    private void AnnounceNotice()
    {
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(EditNoticeText)
            ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(EditNoticeText);

        peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && ComputeStacked(e.NewSize.Width) != _stacked)
        {
            RebuildGrid(e.NewSize.Width, e.NewSize.Height);

            return;
        }

        ApplyVerticalOverflow(e.NewSize.Height);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged -= OnLayoutChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        RebuildGrid();
    }

    private bool ComputeStacked(double width)
    {
        return _viewModel?.Pane is { } pane ? ShouldStack(width, pane) : ShouldStack(width);
    }

    private void RebuildGrid()
    {
        RebuildGrid(ActualWidth, ActualHeight);
    }

    private void RebuildGrid(double width, double height)
    {
        DetachHosts();
        BandsGrid.Children.Clear();
        BandsGrid.ColumnDefinitions.Clear();
        BandsGrid.RowDefinitions.Clear();
        _leafPaths.Clear();
        _splitGrids.Clear();

        if (_viewModel is null)
        {
            return;
        }

        _stacked = ComputeStacked(width);
        _viewModel.SetStacked(_stacked);

        if (_viewModel.Pane is { } pane)
        {
            BandsGrid.Margin = default;
            RebuildTree(pane);
            ApplyVerticalOverflow(height);

            return;
        }

        var bands = _viewModel.Bands;

        BandsGrid.Margin = default;

        if (_stacked)
        {
            ApplyTracks(
                BandsGrid.RowDefinitions,
                bands.Count,
                static () => new RowDefinition(),
                (definition, index) =>
                {
                    definition.Height = GridLength.Auto;
                    definition.MinHeight = bands[index].Width.Length.IsStar ? StarBandMinHeight : 0;
                });
        }
        else
        {
            ApplyTracks(
                BandsGrid.ColumnDefinitions,
                bands.Count,
                static () => new ColumnDefinition(),
                (definition, index) =>
                {
                    var track = bands[index].Width;

                    definition.Width = track.Length;
                    definition.MaxWidth = track.Max;
                    definition.MinWidth = track.Length.IsStar ? StarBandMinWidth : 0;
                });
        }

        for (var index = 0; index < bands.Count; index++)
        {
            var content = BuildBandContent(bands[index]);

            if (_stacked)
            {
                Grid.SetRow(content, index);
            }
            else
            {
                Grid.SetColumn(content, index);
            }

            BandsGrid.Children.Add(content);
        }

        ApplyVerticalOverflow(height);
    }

    private double RequiredHeight()
    {
        if (_stacked || _viewModel is null)
        {
            return 0;
        }

        return _viewModel.Pane is { } pane
            ? pane.MinHeight(StarBandMinHeight)
            : _viewModel.Bands.Select(band => band.MinHeight).DefaultIfEmpty(0).Max();
    }

    private void ApplyVerticalOverflow(double height)
    {
        var required = RequiredHeight();
        var overflows = height > 0 && required > height;

        BandsScroll.VerticalScrollBarVisibility = _stacked || overflows ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        BandsGrid.Height = overflows ? required : double.NaN;
    }

    private void RebuildTree(PaneLayout pane)
    {
        if (_stacked)
        {
            StackLeaves(pane);
            return;
        }

        var content = WrapScrollable(pane);

        if (pane is TilePaneLayout)
        {
            content = new Border { Child = content };
        }

        content.MaxWidth = pane.Width.Max;
        content.MaxHeight = pane.Height.Max;
        content.MinWidth = Math.Min(pane.Width.Min, pane.Width.Max);
        content.MinHeight = Math.Min(pane.Height.Min, pane.Height.Max);
        content.HorizontalAlignment = pane.Width.Length.IsStar ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        BandsGrid.Children.Add(content);
    }

    private void StackLeaves(PaneLayout pane)
    {
        var leaves = new List<TilePaneLayout>();

        CollectLeaves(pane, leaves);

        ApplyTracks(
            BandsGrid.RowDefinitions,
            leaves.Count,
            static () => new RowDefinition(),
            (definition, index) =>
            {
                definition.Height = GridLength.Auto;
                definition.MaxHeight = leaves[index].Height.Max;
                definition.MinHeight = Floor(leaves[index].Height, StarBandMinHeight);
            });

        for (var index = 0; index < leaves.Count; index++)
        {
            var host = GetOrCreateHost(leaves[index].Tile);

            Grid.SetRow(host, index);
            BandsGrid.Children.Add(host);
        }
    }

    private FrameworkElement BuildPaneContent(PaneLayout pane)
    {
        if (pane is EmptyPaneLayout)
        {
            return new Border();
        }

        if (pane is TilePaneLayout leaf)
        {
            _leafPaths[leaf.Tile.TypeId] = leaf.Path;

            return GetOrCreateHost(leaf.Tile);
        }

        var split = (SplitPaneLayout)pane;
        var alongColumns = split.Orientation == SplitOrientation.Columns;
        var grid = new Grid();

        _splitGrids[split] = grid;

        if (alongColumns)
        {
            ApplyTracks(
                grid.ColumnDefinitions,
                split.Children.Count,
                static () => new ColumnDefinition(),
                (definition, index) =>
                {
                    var child = split.Children[index];

                    definition.Width = Track(child, static target => target.Width);
                    definition.MaxWidth = child.Pane.Width.Max;
                    definition.MinWidth = TrackFloor(child, alongColumns: true);
                });
        }
        else
        {
            ApplyTracks(
                grid.RowDefinitions,
                split.Children.Count,
                static () => new RowDefinition(),
                (definition, index) =>
                {
                    var child = split.Children[index];

                    definition.Height = Track(child, static target => target.Height);
                    definition.MaxHeight = child.Pane.Height.Max;
                    definition.MinHeight = TrackFloor(child, alongColumns: false);
                });
        }

        for (var index = 0; index < split.Children.Count; index++)
        {
            var content = WrapScrollable(split.Children[index].Pane);

            if (alongColumns)
            {
                Grid.SetColumn(content, index);
            }
            else
            {
                Grid.SetRow(content, index);
            }

            grid.Children.Add(content);
        }

        if (_viewModel?.IsEditing == true && split.IsComplete)
        {
            AddSplitters(grid, split, alongColumns);
        }

        return grid;
    }

    private void AddSplitters(Grid grid, SplitPaneLayout split, bool alongColumns)
    {
        var added = false;

        for (var index = 1; index < split.Children.Count; index++)
        {
            if (split.Children[index - 1].SizesToContent || split.Children[index].SizesToContent)
            {
                continue;
            }

            var splitter = new GridSplitter
            {
                ResizeBehavior = GridResizeBehavior.PreviousAndCurrent,
                ResizeDirection = alongColumns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                Background = Brushes.Transparent,
            };

            System.Windows.Automation.AutomationProperties.SetName(
                splitter,
                alongColumns ? "Разделитель по вертикали" : "Разделитель по горизонтали");

            if (alongColumns)
            {
                splitter.Width = SplitterThickness;
                splitter.HorizontalAlignment = HorizontalAlignment.Left;
                splitter.VerticalAlignment = VerticalAlignment.Stretch;
                splitter.Margin = new(-SplitterThickness / 2, 0, 0, 0);
                splitter.Cursor = Cursors.SizeWE;
                Grid.SetColumn(splitter, index);
            }
            else
            {
                splitter.Height = SplitterThickness;
                splitter.VerticalAlignment = VerticalAlignment.Top;
                splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
                splitter.Margin = new(0, -SplitterThickness / 2, 0, 0);
                splitter.Cursor = Cursors.SizeNS;
                Grid.SetRow(splitter, index);
            }

            splitter.DragDelta += (_, _) => ShowShareHint(grid, split, alongColumns, index);
            splitter.DragCompleted += (_, args) => CommitShares(grid, split.Path, alongColumns, args.Canceled);

            Panel.SetZIndex(splitter, 1);
            grid.Children.Add(splitter);
            added = true;
        }

        if (added)
        {
            grid.SizeChanged += (_, _) => RefreshSplitters(grid, split, alongColumns);
        }
    }

    private static void RefreshSplitters(Grid grid, SplitPaneLayout split, bool alongColumns)
    {
        foreach (var splitter in grid.Children.OfType<GridSplitter>())
        {
            var index = alongColumns ? Grid.GetColumn(splitter) : Grid.GetRow(splitter);
            var back = Blocked(grid, split, alongColumns, index, false);
            var forward = Blocked(grid, split, alongColumns, index, true);

            var movable = alongColumns ? Cursors.SizeWE : Cursors.SizeNS;

            splitter.Cursor = back.Length > 0 && forward.Length > 0 ? Cursors.No : movable;
            splitter.ToolTip = Obstacle(back, forward);
        }
    }

    private static (double Actual, double Min, double Max) Extent(Grid grid, bool alongColumns, int index)
    {
        if (!alongColumns)
        {
            var row = grid.RowDefinitions[index];

            return (row.ActualHeight, row.MinHeight, row.MaxHeight);
        }

        var column = grid.ColumnDefinitions[index];

        return (column.ActualWidth, column.MinWidth, column.MaxWidth);
    }

    private static string[] Blocked(Grid grid, SplitPaneLayout split, bool alongColumns, int index, bool forward)
    {
        var tracks = alongColumns ? grid.ColumnDefinitions.Count : grid.RowDefinitions.Count;

        if (index < 1 || index >= tracks || index >= split.Children.Count)
        {
            return [];
        }

        var shrinking = forward ? index : index - 1;
        var growing = forward ? index - 1 : index;
        var shrink = Extent(grid, alongColumns, shrinking);
        var grow = Extent(grid, alongColumns, growing);
        var canShrink = shrink.Actual > shrink.Min + RoomTolerance;
        var canGrow = grow.Actual < grow.Max - RoomTolerance;

        if (canShrink && canGrow)
        {
            return [];
        }

        var floor = canShrink ? null : FloorObstacle(split.Children[shrinking], alongColumns);
        var ceiling = canGrow ? null : CeilingObstacle(split.Children[growing].Pane, alongColumns);

        return [.. new[] { floor, ceiling }.OfType<string>()];
    }

    public static string? FloorObstacle(PaneLayoutSlot slot, bool alongColumns)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var floor = TrackFloor(slot, alongColumns);

        if (floor <= 0)
        {
            return null;
        }

        var (required, single) = SmallestFit(slot.Pane, alongColumns);

        if (required + RoomTolerance < floor)
        {
            return alongColumns
                ? $"колонка не бывает уже {floor:0} px"
                : $"строка не бывает ниже {floor:0} px";
        }

        if (single is { } leaf)
        {
            return $"плитка «{leaf.Tile.Title}» уже на минимуме";
        }

        return alongColumns
            ? "несколько плиток подряд уже на минимуме ширины"
            : "несколько плиток одна под другой уже на минимуме высоты";
    }

    public static string? CeilingObstacle(PaneLayout pane, bool alongColumns)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var (allowed, single) = LargestFit(pane, alongColumns);

        if (!double.IsFinite(allowed))
        {
            return null;
        }

        if (single is { } leaf)
        {
            return alongColumns
                ? $"плитка «{leaf.Tile.Title}» не шире {allowed:0} px"
                : $"плитка «{leaf.Tile.Title}» не выше {allowed:0} px";
        }

        return alongColumns
            ? $"плитки подряд вместе не шире {allowed:0} px"
            : $"плитки одна под другой вместе не выше {allowed:0} px";
    }

    private static (double Amount, TilePaneLayout? Single) SmallestFit(PaneLayout pane, bool alongColumns)
    {
        var amount = Axis(pane, alongColumns).Min;

        switch (pane)
        {
            case TilePaneLayout leaf:
                return (amount, leaf);

            case SplitPaneLayout split:
            {
                var parts = split.Children.Select(child => SmallestFit(child.Pane, alongColumns)).ToArray();

                if ((split.Orientation == SplitOrientation.Columns) != alongColumns)
                {
                    return (amount, parts.MaxBy(part => part.Amount).Single);
                }

                var holding = parts.Where(part => part.Amount > 0).ToArray();

                return (amount, holding.Length == 1 ? holding[0].Single : null);
            }

            default:
                return (amount, null);
        }
    }

    private static (double Amount, TilePaneLayout? Single) LargestFit(PaneLayout pane, bool alongColumns)
    {
        var amount = Axis(pane, alongColumns).Max;

        switch (pane)
        {
            case TilePaneLayout leaf:
                return (amount, leaf);

            case SplitPaneLayout split:
            {
                var parts = split.Children.Select(child => LargestFit(child.Pane, alongColumns)).ToArray();

                if ((split.Orientation == SplitOrientation.Columns) != alongColumns)
                {
                    return (amount, parts.MaxBy(part => part.Amount).Single);
                }

                var holding = parts.Where(part => double.IsFinite(part.Amount)).ToArray();

                return (amount, holding.Length == 1 ? holding[0].Single : null);
            }

            default:
                return (amount, null);
        }
    }

    private static TrackSize Axis(PaneLayout pane, bool alongColumns)
    {
        return alongColumns ? pane.Width : pane.Height;
    }

    private static string? Obstacle(IEnumerable<string> back, IEnumerable<string> forward)
    {
        var reasons = back
            .Concat(forward)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return reasons.Length == 0 ? null : $"Разделитель упирается: {string.Join("; ", reasons)}";
    }

    private void CommitShares(Grid grid, int[] path, bool alongColumns, bool canceled)
    {
        HideOverlay();

        if (canceled || _viewModel?.Resize(path, Shares(grid, alongColumns)) != true)
        {
            RebuildGrid();
        }
    }

    private void ShowShareHint(Grid grid, SplitPaneLayout split, bool alongColumns, int index)
    {
        var shares = Shares(grid, alongColumns);

        if (index >= shares.Length)
        {
            return;
        }

        var obstacle = Obstacle(
            Blocked(grid, split, alongColumns, index, false),
            Blocked(grid, split, alongColumns, index, true));

        if (_shareHint is null)
        {
            _shareHint = new()
            {
                Padding = new(8, 4, 8, 4),
            };

            _shareHint.SetResourceReference(TextBlock.BackgroundProperty, "Bg.Surface");
            _shareHint.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Primary");
        }

        if (!EditOverlay.Children.Contains(_shareHint))
        {
            EditOverlay.Children.Add(_shareHint);
        }

        var proportions = $"{shares[index - 1] * 100:0} % / {shares[index] * 100:0} %";

        _shareHint.Text = obstacle is null ? proportions : $"{proportions}{Environment.NewLine}{obstacle}";

        var position = Mouse.GetPosition(EditOverlay);

        Canvas.SetLeft(_shareHint, position.X + 12);
        Canvas.SetTop(_shareHint, position.Y + 12);
    }

    private FrameworkElement WrapScrollable(PaneLayout pane)
    {
        var element = BuildPaneContent(pane);

        return pane is SplitPaneLayout && pane.Scrollable ? Wrap(element) : element;
    }

    private FrameworkElement BuildBandContent(TileBand band)
    {
        var grid = BuildBand(band);

        if (_stacked)
        {
            return grid;
        }

        return new Border
        {
            MaxWidth = band.Width.Max,
            Child = Scrollable(band) ? Wrap(grid) : grid,
        };
    }

    private Grid BuildBand(TileBand band)
    {
        var grid = new Grid();

        ApplyTracks(
            grid.ColumnDefinitions,
            band.Columns.Count,
            static () => new ColumnDefinition(),
            (definition, index) =>
            {
                definition.Width = _stacked ? new(1, GridUnitType.Star) : band.Columns[index].Length;
                definition.MaxWidth = _stacked ? double.PositiveInfinity : band.Columns[index].Max;
            });

        ApplyTracks(
            grid.RowDefinitions,
            band.Rows.Count,
            static () => new RowDefinition(),
            (definition, index) =>
            {
                definition.Height = band.Rows[index].Length;
                definition.MaxHeight = band.Rows[index].Max;
                definition.MinHeight = Math.Min(band.Rows[index].Min, band.Rows[index].Max);
            });

        foreach (var slot in band.Tiles)
        {
            var host = GetOrCreateHost(slot.Tile);

            Grid.SetRow(host, slot.Row);
            Grid.SetColumn(host, slot.Column);
            Grid.SetRowSpan(host, slot.RowSpan);
            Grid.SetColumnSpan(host, slot.ColumnSpan);

            grid.Children.Add(host);
        }

        return grid;
    }

    private void DetachHosts()
    {
        foreach (var host in _hosts.Values)
        {
            switch (host.Parent)
            {
                case Grid parent:
                    parent.Children.Remove(host);
                    break;

                case Border border:
                    border.Child = null;
                    break;

                case ScrollViewer viewer:
                    viewer.Content = null;
                    break;
            }
        }
    }

    private ContentControl GetOrCreateHost(DashboardTileViewModel tile)
    {
        if (_hosts.TryGetValue(tile, out var existing))
        {
            return existing;
        }

        var host = new ContentControl
        {
            Content = tile,
            ContentTemplate = (DataTemplate)Resources["DashboardTileChrome"],
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        _hosts[tile] = host;
        return host;
    }

    private void OnShieldMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border shield || shield.DataContext is not DashboardTileViewModel tile)
        {
            return;
        }

        shield.Focus();

        _dragTile = tile;
        _dragShield = shield;
        _dragOrigin = e.GetPosition(this);
        _dragging = false;
    }

    private void OnShieldMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTile is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (!_dragging)
        {
            if (Math.Abs(position.X - _dragOrigin.X) < DragThreshold && Math.Abs(position.Y - _dragOrigin.Y) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _dragShield?.CaptureMouse();
        }

        ShowDropHint(position);
    }

    private void OnShieldMouseUp(object sender, MouseButtonEventArgs e)
    {
        var dragging = _dragging;
        var source = _dragTile;
        var position = e.GetPosition(this);

        EndDrag();

        if (!dragging || source is null || _viewModel is null)
        {
            return;
        }

        if (TargetAt(position) is not { } target || string.Equals(target.Tile.TypeId, source.TypeId, StringComparison.Ordinal))
        {
            return;
        }

        var side = Side(position, target.Bounds);

        var moved = side == PaneSide.None
            ? _viewModel.Swap(source.TypeId, target.Tile.TypeId)
            : _viewModel.Move(source.TypeId, target.Tile.TypeId, side);

        if (!moved)
        {
            _viewModel.ShowEditNotice("Плитку не получилось перенести на это место.");
        }
    }

    private void ShowDropHint(Point position)
    {
        if (TargetAt(position) is not { } target)
        {
            HideOverlay();

            return;
        }

        _dropHint ??= new()
        {
            BorderThickness = new(2),
            IsHitTestVisible = false,
        };

        _dropHint.SetResourceReference(Border.BorderBrushProperty, "Accent.Base");
        _dropHint.SetResourceReference(Border.BackgroundProperty, "Bg.Subtle");
        _dropHint.Opacity = 0.6;

        if (!EditOverlay.Children.Contains(_dropHint))
        {
            EditOverlay.Children.Add(_dropHint);
        }

        var bounds = target.Bounds;
        var side = Side(position, bounds);

        var rect = side switch
        {
            PaneSide.Left => new Rect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height),
            PaneSide.Right => new Rect(bounds.X + (bounds.Width / 2), bounds.Y, bounds.Width / 2, bounds.Height),
            PaneSide.Top => new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height / 2),
            PaneSide.Bottom => new Rect(bounds.X, bounds.Y + (bounds.Height / 2), bounds.Width, bounds.Height / 2),
            _ => bounds,
        };

        _dropHint.Width = rect.Width;
        _dropHint.Height = rect.Height;

        Canvas.SetLeft(_dropHint, rect.X);
        Canvas.SetTop(_dropHint, rect.Y);
    }

    private DropTarget? TargetAt(Point position)
    {
        if (VisualTreeHelper.HitTest(this, position)?.VisualHit is not DependencyObject hit)
        {
            return null;
        }

        while (hit is not null)
        {
            if (hit is FrameworkElement { DataContext: DashboardTileViewModel tile } element && _hosts.ContainsKey(tile))
            {
                var host = _hosts[tile];
                var origin = host.TransformToAncestor(this).Transform(new(0, 0));

                return new(tile, new(origin, new Size(host.ActualWidth, host.ActualHeight)), element);
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    private void EndDrag()
    {
        _dragShield?.ReleaseMouseCapture();
        _dragShield = null;
        _dragTile = null;
        _dragging = false;

        HideOverlay();
    }

    private void HideOverlay()
    {
        EditOverlay.Children.Clear();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsEditing: true } viewModel)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (e.Key)
        {
            case Key.Escape when _dragging:
                EndDrag();
                e.Handled = true;
                return;

            case Key.Escape:
                viewModel.StopEditing();
                e.Handled = true;
                return;

            case Key.Z when control:
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                return;

            case Key.Delete when FocusedTile() is { } removed:
                viewModel.RemoveTileCommand.Execute(removed.TypeId);
                e.Handled = true;
                return;

            case Key.Left or Key.Right or Key.Up or Key.Down when control:
                e.Handled = MoveOrResize(e.Key, shift);
                return;
        }
    }

    private bool MoveOrResize(Key key, bool move)
    {
        if (FocusedTile() is not { } tile || _viewModel is null)
        {
            return false;
        }

        if (!move)
        {
            return AdjustShare(tile, key);
        }

        if (TargetFor(key) is not { } neighbour)
        {
            _viewModel.ShowEditNotice("Перенести плитку некуда: с этой стороны соседей нет.");

            return true;
        }

        if (!_viewModel.Move(tile.TypeId, neighbour, SideOf(key)))
        {
            _viewModel.ShowEditNotice("Плитку не получилось перенести на это место.");
        }

        return true;
    }

    private static PaneSide SideOf(Key key)
    {
        return key switch
        {
            Key.Left => PaneSide.Left,
            Key.Right => PaneSide.Right,
            Key.Up => PaneSide.Top,
            _ => PaneSide.Bottom,
        };
    }

    private string? TargetFor(Key key)
    {
        if (FocusedTile() is not { } tile || !_leafPaths.TryGetValue(tile.TypeId, out var path) || _viewModel?.Pane is not { } root)
        {
            return null;
        }

        var alongColumns = key is Key.Left or Key.Right;
        var back = key is Key.Left or Key.Up;

        for (var depth = path.Length - 1; depth >= 0; depth--)
        {
            if (FindPane(root, path[..depth]) is not SplitPaneLayout node
                || (node.Orientation == SplitOrientation.Columns) != alongColumns)
            {
                continue;
            }

            var index = IndexOfChild(node, path[..(depth + 1)]);

            if (index < 0)
            {
                return null;
            }

            var neighbour = index + (back ? -1 : 1);

            if (neighbour < 0 || neighbour >= node.Children.Count)
            {
                return null;
            }

            var leaves = new List<TilePaneLayout>();

            CollectLeaves(node.Children[neighbour].Pane, leaves);

            return leaves.Count > 0 ? leaves[0].Tile.TypeId : null;
        }

        return null;
    }

    private bool AdjustShare(DashboardTileViewModel tile, Key key)
    {
        if (!_leafPaths.TryGetValue(tile.TypeId, out var path) || _viewModel is not { Pane: { } root } viewModel)
        {
            return false;
        }

        var alongColumns = key is Key.Left or Key.Right;
        var step = key is Key.Left or Key.Up ? -ShareStep : ShareStep;

        for (var depth = path.Length - 1; depth >= 0; depth--)
        {
            var nodePath = path[..depth];

            if (FindPane(root, nodePath) is not SplitPaneLayout node
                || (node.Orientation == SplitOrientation.Columns) != alongColumns
                || node.Children.Count < 2)
            {
                continue;
            }

            if (AdjustAt(node, nodePath, path[..(depth + 1)], tile, alongColumns, step) is { } reason)
            {
                viewModel.ShowEditNotice(reason);
            }

            return true;
        }

        viewModel.ShowEditNotice(alongColumns
            ? "Слева и справа от этой плитки соседей нет."
            : "Сверху и снизу от этой плитки соседей нет.");

        return true;
    }

    private string? AdjustAt(
        SplitPaneLayout node,
        int[] nodePath,
        int[] childPath,
        DashboardTileViewModel tile,
        bool alongColumns,
        double step)
    {
        var index = IndexOfChild(node, childPath);

        if (!node.IsComplete || index < 0)
        {
            return ResizeRefused;
        }

        if (node.Children[index].SizesToContent)
        {
            return alongColumns
                ? $"Ширина плитки «{tile.Title}» подстроена под содержимое и не меняется."
                : $"Высота плитки «{tile.Title}» подстроена под содержимое и не меняется.";
        }

        var neighbour = Neighbour(node, index);

        if (neighbour < 0)
        {
            return alongColumns
                ? "Рядом нет плитки, ширину которой можно изменить."
                : "Рядом нет плитки, высоту которой можно изменить.";
        }

        if (Resistance(node, alongColumns, index, neighbour, step) is { } obstacle)
        {
            return obstacle;
        }

        var weights = node.Children.Select(child => child.Weight).ToArray();
        var total = weights.Sum();

        if (total <= 0)
        {
            return ResizeRefused;
        }

        weights[index] = (weights[index] / total) + step;
        weights[neighbour] = (weights[neighbour] / total) - step;

        if (weights[index] <= 0)
        {
            return $"Плитке «{tile.Title}» уже некуда уступать место.";
        }

        if (weights[neighbour] <= 0)
        {
            return "Соседней плитке не останется места.";
        }

        return _viewModel?.Resize(nodePath, weights) == true ? null : ResizeRefused;
    }

    private string? Resistance(SplitPaneLayout node, bool alongColumns, int index, int neighbour, double step)
    {
        if (Math.Abs(index - neighbour) != 1 || !_splitGrids.TryGetValue(node, out var grid) || !HasReliableLayout(grid))
        {
            return null;
        }

        var boundary = Math.Max(index, neighbour);
        var shrinking = step > 0 ? neighbour : index;

        return Obstacle(Blocked(grid, node, alongColumns, boundary, shrinking == boundary), []);
    }

    public static bool HasReliableLayout(Grid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        return grid is { IsMeasureValid: true, IsArrangeValid: true, ActualWidth: > 0, ActualHeight: > 0 };
    }

    private static int Neighbour(SplitPaneLayout node, int index)
    {
        for (var candidate = index + 1; candidate < node.Children.Count; candidate++)
        {
            if (!node.Children[candidate].SizesToContent)
            {
                return candidate;
            }
        }

        for (var candidate = index - 1; candidate >= 0; candidate--)
        {
            if (!node.Children[candidate].SizesToContent)
            {
                return candidate;
            }
        }

        return -1;
    }

    private static int IndexOfChild(SplitPaneLayout node, int[] childPath)
    {
        for (var index = 0; index < node.Children.Count; index++)
        {
            if (node.Children[index].Pane.Path.AsSpan().SequenceEqual(childPath))
            {
                return index;
            }
        }

        return -1;
    }

    private static PaneLayout? FindPane(PaneLayout root, int[] path)
    {
        if (root.Path.AsSpan().SequenceEqual(path))
        {
            return root;
        }

        if (root is not SplitPaneLayout split)
        {
            return null;
        }

        foreach (var child in split.Children)
        {
            if (FindPane(child.Pane, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private DashboardTileViewModel? FocusedTile()
    {
        return Keyboard.FocusedElement is FrameworkElement { DataContext: DashboardTileViewModel tile } ? tile : null;
    }

    private sealed record DropTarget(DashboardTileViewModel Tile, Rect Bounds, FrameworkElement Element);
}
