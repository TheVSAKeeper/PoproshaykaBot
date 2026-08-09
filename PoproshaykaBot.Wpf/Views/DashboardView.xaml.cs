using KeepShell.Bootstrap;
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

    private static bool Scrollable(TileBand band)
    {
        return band.Rows.All(row => row.Length.IsAuto) && band.Columns.All(column => column.Length.IsAuto);
    }

    private static FrameworkElement Wrap(Grid grid)
    {
        return new ScrollViewer
        {
            Content = grid,
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
        if (!e.WidthChanged)
        {
            return;
        }

        var stacked = ShouldStack(e.NewSize.Width);

        if (stacked == _stacked)
        {
            return;
        }

        _stacked = stacked;
        RebuildGrid();
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

    private void RebuildGrid()
    {
        DetachHosts();
        BandsGrid.Children.Clear();
        BandsGrid.ColumnDefinitions.Clear();
        BandsGrid.RowDefinitions.Clear();

        if (_viewModel is null)
        {
            return;
        }

        var bands = _viewModel.Bands;

        BandsScroll.VerticalScrollBarVisibility = _stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
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
            if (host.Parent is Grid parent)
            {
                parent.Children.Remove(host);
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
