using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class DashboardView : UserControl, IView<DashboardViewModel>
{
    private readonly Dictionary<DashboardTileViewModel, ContentControl> _hosts = [];
    private DashboardViewModel? _viewModel;

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
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
        if (_viewModel is null)
        {
            TilesGrid.Children.Clear();
            TilesGrid.ColumnDefinitions.Clear();
            TilesGrid.RowDefinitions.Clear();
            return;
        }

        SyncDefinitions();

        var placedTiles = new HashSet<DashboardTileViewModel>();

        foreach (var placement in _viewModel.Placements)
        {
            var host = GetOrCreateHost(placement.Tile);

            Grid.SetRow(host, placement.Row);
            Grid.SetColumn(host, placement.Column);
            Grid.SetRowSpan(host, placement.RowSpan);
            Grid.SetColumnSpan(host, placement.ColumnSpan);

            if (!TilesGrid.Children.Contains(host))
            {
                TilesGrid.Children.Add(host);
            }

            placedTiles.Add(placement.Tile);
        }

        foreach (var (tile, host) in _hosts)
        {
            if (!placedTiles.Contains(tile))
            {
                TilesGrid.Children.Remove(host);
            }
        }
    }

    private void SyncDefinitions()
    {
        var columnWidths = _viewModel!.ColumnWidths;
        var rowHeights = _viewModel.RowHeights;

        while (TilesGrid.ColumnDefinitions.Count > columnWidths.Count)
        {
            TilesGrid.ColumnDefinitions.RemoveAt(TilesGrid.ColumnDefinitions.Count - 1);
        }

        while (TilesGrid.ColumnDefinitions.Count < columnWidths.Count)
        {
            TilesGrid.ColumnDefinitions.Add(new());
        }

        for (var i = 0; i < columnWidths.Count; i++)
        {
            TilesGrid.ColumnDefinitions[i].Width = columnWidths[i];
        }

        while (TilesGrid.RowDefinitions.Count > rowHeights.Count)
        {
            TilesGrid.RowDefinitions.RemoveAt(TilesGrid.RowDefinitions.Count - 1);
        }

        while (TilesGrid.RowDefinitions.Count < rowHeights.Count)
        {
            TilesGrid.RowDefinitions.Add(new());
        }

        for (var i = 0; i < rowHeights.Count; i++)
        {
            TilesGrid.RowDefinitions[i].Height = rowHeights[i];
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
