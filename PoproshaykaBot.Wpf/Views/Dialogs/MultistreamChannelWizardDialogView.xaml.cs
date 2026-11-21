using System.Windows.Controls;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;

namespace PoproshaykaBot.Wpf.Views.Dialogs;

public partial class MultistreamChannelWizardDialogView : UserControl, IView<MultistreamChannelWizardDialogViewModel>
{
    public MultistreamChannelWizardDialogView()
    {
        InitializeComponent();
    }
}
