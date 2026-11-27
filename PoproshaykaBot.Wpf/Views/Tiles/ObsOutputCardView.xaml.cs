using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ObsOutputCardView : UserControl, IView<ObsOutputCardViewModel>
{
    public ObsOutputCardView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ObsOutputCardViewModel { IsStopConfirmationArmed: true } card)
        {
            card.CancelStopConfirmationCommand.Execute(null);
            e.Handled = true;
        }
    }
}
