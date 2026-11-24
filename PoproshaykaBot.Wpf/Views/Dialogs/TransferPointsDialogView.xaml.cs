using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Dialogs;

public partial class TransferPointsDialogView : UserControl, IView<TransferPointsDialogViewModel>
{
    public TransferPointsDialogView()
    {
        InitializeComponent();
    }
}
