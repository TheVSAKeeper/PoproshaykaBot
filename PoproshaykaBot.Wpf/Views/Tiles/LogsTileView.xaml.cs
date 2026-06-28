using System.Windows.Controls;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class LogsTileView : UserControl, IView<LogsTileViewModel>
{
    public LogsTileView()
    {
        InitializeComponent();
    }
}
