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
    private const double StarBandMinHeight = 320;
    private const double SplitterThickness = 6;
    private const double DragThreshold = 6;
    private const double SwapZone = 0.3;
    private const double ShareStep = 0.05;

    private readonly Dictionary<DashboardTileViewModel, ContentControl> _hosts = [];
    private readonly Dictionary<string, int[]> _leafPaths = new(StringComparer.Ordinal);

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

    private static double Floor(GridLength length, TrackSize track, double minimum)
    {
        return length.IsStar ? Math.Min(minimum, track.Max) : 0;
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
        }

        _viewModel = e.NewValue as DashboardViewModel;

        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged += OnLayoutChanged;
        }

        RebuildGrid();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || ComputeStacked(e.NewSize.Width) == _stacked)
        {
            return;
        }

        RebuildGrid(e.NewSize.Width);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged -= OnLayoutChanged;
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
        RebuildGrid(ActualWidth);
    }

    private void RebuildGrid(double width)
    {
        DetachHosts();
        BandsGrid.Children.Clear();
        BandsGrid.ColumnDefinitions.Clear();
        BandsGrid.RowDefinitions.Clear();
        _leafPaths.Clear();

        if (_viewModel is null)
        {
            return;
        }

        _stacked = ComputeStacked(width);
        _viewModel.SetStacked(_stacked);

        BandsScroll.VerticalScrollBarVisibility = _stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        if (_viewModel.Pane is { } pane)
        {
            BandsGrid.Margin = new(0, 0, 0, -1);
            RebuildTree(pane);
            return;
        }

        var bands = _viewModel.Bands;

        BandsGrid.Margin = _stacked ? new(0, 0, 0, -1) : new(0, 0, -1, -1);

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
        if (pane is TilePaneLayout leaf)
        {
            _leafPaths[leaf.Tile.TypeId] = leaf.Path;

            return GetOrCreateHost(leaf.Tile);
        }

        var split = (SplitPaneLayout)pane;
        var alongColumns = split.Orientation == SplitOrientation.Columns;
        var grid = new Grid();

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
                    definition.MinWidth = Floor(definition.Width, child.Pane.Width, StarBandMinWidth);
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
                    definition.MinHeight = Floor(definition.Height, child.Pane.Height, StarBandMinHeight);
                });
        }

        for (var index = 0; index < split.Children.Count; index++)
        {
            var content = BuildChildContent(split.Children[index].Pane, alongColumns, index == split.Children.Count - 1);

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
            AddSplitters(grid, split.Path, alongColumns, split.Children.Count);
        }

        return grid;
    }

    private void AddSplitters(Grid grid, int[] path, bool alongColumns, int count)
    {
        for (var index = 1; index < count; index++)
        {
            var splitter = new GridSplitter
            {
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
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

            splitter.DragDelta += (_, _) => ShowShareHint(grid, alongColumns, index);
            splitter.DragCompleted += (_, args) => CommitShares(grid, path, alongColumns, args.Canceled);

            Panel.SetZIndex(splitter, 1);
            grid.Children.Add(splitter);
        }
    }

    private void CommitShares(Grid grid, int[] path, bool alongColumns, bool canceled)
    {
        HideOverlay();

        if (canceled)
        {
            RebuildGrid();

            return;
        }

        _viewModel?.Resize(path, Shares(grid, alongColumns));
    }

    private void ShowShareHint(Grid grid, bool alongColumns, int index)
    {
        var shares = Shares(grid, alongColumns);

        if (index >= shares.Length)
        {
            return;
        }

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

        _shareHint.Text = $"{shares[index - 1] * 100:0} % / {shares[index] * 100:0} %";

        var position = Mouse.GetPosition(EditOverlay);

        Canvas.SetLeft(_shareHint, position.X + 12);
        Canvas.SetTop(_shareHint, position.Y + 12);
    }

    private FrameworkElement WrapScrollable(PaneLayout pane)
    {
        var element = BuildPaneContent(pane);

        return pane is SplitPaneLayout && pane.Scrollable ? Wrap(element) : element;
    }

    private FrameworkElement BuildChildContent(PaneLayout pane, bool alongColumns, bool last)
    {
        var content = WrapScrollable(pane);

        if (!alongColumns || last)
        {
            return content;
        }

        var seam = new Border
        {
            BorderThickness = new(0, 0, 1, 0),
            Child = content,
        };

        seam.SetResourceReference(Border.BorderBrushProperty, "Border.Subtle");

        return seam;
    }

    private FrameworkElement BuildBandContent(TileBand band)
    {
        var grid = BuildBand(band);

        if (_stacked)
        {
            return grid;
        }

        var seam = new Border
        {
            BorderThickness = new(0, 0, 1, 0),
            MaxWidth = band.Width.Max,
            Child = Scrollable(band) ? Wrap(grid) : grid,
        };

        seam.SetResourceReference(Border.BorderBrushProperty, "Border.Subtle");

        return seam;
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

    private void OnAddTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
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

        if (side == PaneSide.None)
        {
            _viewModel.Swap(source.TypeId, target.Tile.TypeId);

            return;
        }

        _viewModel.Move(source.TypeId, target.Tile.TypeId, side);
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

        if (move)
        {
            return TargetFor(key) is { } neighbour && _viewModel.Move(tile.TypeId, neighbour, SideOf(key));
        }

        return AdjustShare(tile, key);
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
        if (!_leafPaths.TryGetValue(tile.TypeId, out var path) || _viewModel?.Pane is not { } root)
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

            var index = IndexOfChild(node, path[..(depth + 1)]);

            if (!node.IsComplete || index < 0)
            {
                return false;
            }

            var neighbour = index + 1 < node.Children.Count ? index + 1 : index - 1;
            var weights = node.Children.Select(child => child.Weight).ToArray();
            var total = weights.Sum();

            if (total <= 0)
            {
                return false;
            }

            weights[index] = (weights[index] / total) + step;
            weights[neighbour] = (weights[neighbour] / total) - step;

            if (weights[index] <= 0 || weights[neighbour] <= 0)
            {
                return false;
            }

            return _viewModel.Resize(nodePath, weights);
        }

        return false;
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
