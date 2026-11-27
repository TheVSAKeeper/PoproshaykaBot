using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class StreamAndChannelsTileView : UserControl, IView<StreamAndChannelsTileViewModel>
{
    public StreamAndChannelsTileView()
    {
        InitializeComponent();
    }
}
