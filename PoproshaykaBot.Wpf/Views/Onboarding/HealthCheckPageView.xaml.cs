using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.Views.Onboarding;

public partial class HealthCheckPageView : UserControl, IView<HealthCheckPageViewModel>
{
    public HealthCheckPageView()
    {
        InitializeComponent();
    }

    private void OnChatTestInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return))
        {
            return;
        }

        e.Handled = true;
        if (DataContext is HealthCheckPageViewModel viewModel
            && !string.IsNullOrWhiteSpace(viewModel.ChatTestMessage)
            && viewModel.SendChatTestCommand.CanExecute(null))
        {
            viewModel.SendChatTestCommand.Execute(null);
        }
    }
}
