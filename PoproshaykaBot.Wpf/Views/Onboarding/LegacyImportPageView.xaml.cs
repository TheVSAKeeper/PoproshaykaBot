using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.Views.Onboarding;

public partial class LegacyImportPageView : UserControl, IView<LegacyImportPageViewModel>
{
    public LegacyImportPageView()
    {
        InitializeComponent();
    }

    private void OnSourceListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && DataContext is LegacyImportPageViewModel { Import: { } import })
        {
            e.Handled = true;
            if (import.ImportCommand.CanExecute(null))
            {
                import.ImportCommand.Execute(null);
            }
        }
    }
}
