using System.Windows.Controls;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class BotMessagingLinksView : UserControl, IView<BotMessagingLinksViewModel>
{
    public BotMessagingLinksView() => InitializeComponent();
}
