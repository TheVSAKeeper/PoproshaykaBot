using KeepShell.Bootstrap;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class DashboardView : UserControl, IView<DashboardViewModel>
{
    private const double StackedWidthThreshold = 720;
    private const double StarBandMinWidth = 320;
    private const double StarBandMinHeight = 320;

    private readonly Dictionary<DashboardTileViewModel, ContentControl> _hosts = [];
    private DashboardViewModel? _viewModel;
    private bool _stacked;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
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
        return track.Length.IsStar ? Math.Min(minimum, track.Max) : 0;
    }

    private static GridLength Track(PaneLayoutSlot slot, Func<PaneLayout, TrackSize> axis)
    {
        return axis(slot.Pane).Length.IsStar ? new(slot.Weight, GridUnitType.Star) : GridLength.Auto;
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

        if (_viewModel is null)
        {
            return;
        }

        _stacked = ComputeStacked(width);

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
                    definition.MinWidth = Floor(child.Pane.Width, StarBandMinWidth);
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
                    definition.MinHeight = Floor(child.Pane.Height, StarBandMinHeight);
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

        return grid;
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
}
