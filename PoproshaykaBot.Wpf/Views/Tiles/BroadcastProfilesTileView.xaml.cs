using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class BroadcastProfilesTileView : UserControl, IView<BroadcastProfilesTileViewModel>
{
    public BroadcastProfilesTileView()
    {
        InitializeComponent();
    }
}
