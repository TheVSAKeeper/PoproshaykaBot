using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class StreamPreviewTileView : UserControl, IView<StreamPreviewTileViewModel>
{
    public StreamPreviewTileView()
    {
        InitializeComponent();
    }
}
