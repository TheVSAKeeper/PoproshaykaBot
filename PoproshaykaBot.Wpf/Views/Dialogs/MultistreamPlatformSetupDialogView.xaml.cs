using System.Windows.Controls;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;

namespace PoproshaykaBot.Wpf.Views.Dialogs;

public partial class MultistreamPlatformSetupDialogView : UserControl, IView<MultistreamPlatformSetupDialogViewModel>
{
    public MultistreamPlatformSetupDialogView()
    {
        InitializeComponent();
    }
}
