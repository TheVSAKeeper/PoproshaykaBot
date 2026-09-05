using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class DebugBannerView : UserControl, IView<DebugBannerViewModel>
{
    public DebugBannerView()
    {
        InitializeComponent();
    }
}
