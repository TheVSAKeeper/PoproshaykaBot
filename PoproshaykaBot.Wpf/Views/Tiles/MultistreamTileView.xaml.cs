using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class MultistreamTileView : UserControl, IView<MultistreamTileViewModel>
{
    public MultistreamTileView()
    {
        InitializeComponent();
        Loaded += (_, _) => ReportVisibility();
        Unloaded += (_, _) => (DataContext as MultistreamTileViewModel)?.SetOnScreen(false);
        IsVisibleChanged += (_, _) => ReportVisibility();
        DataContextChanged += (_, args) =>
        {
            (args.OldValue as MultistreamTileViewModel)?.SetOnScreen(false);
            ReportVisibility();
        };
    }

    private void ReportVisibility()
    {
        if (DataContext is MultistreamTileViewModel tile)
        {
            tile.SetOnScreen(IsLoaded && IsVisible);
        }
    }
}
