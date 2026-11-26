using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Onboarding;

public partial class LegacyImportPageView : UserControl, IView<LegacyImportPageViewModel>
{
    public LegacyImportPageView()
    {
        InitializeComponent();
    }
}
