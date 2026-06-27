using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class SpikePageView : UserControl, IView<SpikePageViewModel>
{
    public SpikePageView()
    {
        InitializeComponent();
    }
}
