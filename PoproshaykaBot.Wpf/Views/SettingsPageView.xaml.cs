using PoproshaykaBot.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class SettingsPageView : UserControl, IView<SettingsPageViewModel>
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is not TextBox)
        {
            e.Handled = true;
        }
    }
}
