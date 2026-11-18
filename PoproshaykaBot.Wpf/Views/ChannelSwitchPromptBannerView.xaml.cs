using System.Windows.Controls;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Views;

public partial class ChannelSwitchPromptBannerView : UserControl, IView<ChannelSwitchPromptBannerViewModel>
{
    public ChannelSwitchPromptBannerView()
    {
        InitializeComponent();
    }
}
