using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class DashboardView : UserControl, IView<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
