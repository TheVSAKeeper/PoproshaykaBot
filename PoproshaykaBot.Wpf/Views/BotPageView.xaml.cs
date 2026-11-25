using System.Windows.Controls;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Views;

public partial class BotPageView : UserControl, IView<BotPageViewModel>
{
    public BotPageView() => InitializeComponent();
}
