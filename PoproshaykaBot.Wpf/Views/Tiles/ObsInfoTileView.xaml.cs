using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ObsInfoTileView : UserControl, IView<ObsInfoTileViewModel>
{
    public const double TwoColumnsWidth = 340;

    private double _outputsWidth;

    public ObsInfoTileView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;
        ApplyOutputColumns();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        _ = Dispatcher.BeginInvoke(ApplyOutputColumns);
    }

    private void OnOutputsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _outputsWidth = e.NewSize.Width;
        ApplyOutputColumns();
    }

    private void ApplyOutputColumns()
    {
        var twoColumns = _outputsWidth >= TwoColumnsWidth * FontScaleManager.Current;

        if (twoColumns)
        {
            SecondOutputColumn.Width = new(1, GridUnitType.Star);
            StreamCardHost.Margin = new(0, 0, 3, 0);
            Grid.SetRow(RecordCardHost, 0);
            Grid.SetColumn(RecordCardHost, 1);
            RecordCardHost.Margin = new(3, 0, 0, 0);
        }
        else
        {
            SecondOutputColumn.Width = new(0);
            StreamCardHost.Margin = new(0, 0, 0, 6);
            Grid.SetRow(RecordCardHost, 1);
            Grid.SetColumn(RecordCardHost, 0);
            RecordCardHost.Margin = new(0);
        }
    }
}
