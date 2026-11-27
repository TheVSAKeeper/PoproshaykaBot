using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ObsSecondaryOutputView : UserControl, IView<ObsSecondaryOutputViewModel>
{
    public ObsSecondaryOutputView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ObsSecondaryOutputViewModel { IsStopConfirmationArmed: true } row)
        {
            row.CancelStopConfirmationCommand.Execute(null);
            e.Handled = true;
        }
    }
}
