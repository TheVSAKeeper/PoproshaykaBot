using PoproshaykaBot.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class DashboardView : UserControl, IView<DashboardViewModel>
{
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
        TilesGrid.Children.Clear();
        TilesGrid.ColumnDefinitions.Clear();
        TilesGrid.RowDefinitions.Clear();

        if (_viewModel is null)
        {
            return;
        }

        foreach (var width in _viewModel.ColumnWidths)
        {
            TilesGrid.ColumnDefinitions.Add(new() { Width = width });
        }

        foreach (var height in _viewModel.RowHeights)
        {
            TilesGrid.RowDefinitions.Add(new() { Height = height });
        }

        var template = (DataTemplate)Resources["DashboardTileChrome"];

        foreach (var placement in _viewModel.Placements)
        {
            var host = new ContentControl
            {
                Content = placement.Tile,
                ContentTemplate = template,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };

            Grid.SetRow(host, placement.Row);
            Grid.SetColumn(host, placement.Column);
            Grid.SetRowSpan(host, placement.RowSpan);
            Grid.SetColumnSpan(host, placement.ColumnSpan);

            TilesGrid.Children.Add(host);
        }
    }
}
