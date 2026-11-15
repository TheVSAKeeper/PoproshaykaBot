using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class PlatformOAuthAccountView : UserControl, IView<PlatformOAuthAccountViewModel>
{
    public PlatformOAuthAccountView()
    {
        InitializeComponent();
    }
}
