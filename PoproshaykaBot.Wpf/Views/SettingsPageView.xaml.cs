using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class SettingsPageView : UserControl, IView<SettingsPageViewModel>
{
    public SettingsPageView()
    {
        InitializeComponent();
    }
}
