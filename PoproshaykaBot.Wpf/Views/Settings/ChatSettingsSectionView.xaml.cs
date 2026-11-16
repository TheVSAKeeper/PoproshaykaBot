using KeepShell.Services;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class ChatSettingsSectionView : UserControl, IView<ChatSettingsSectionViewModel>
{
    public ChatSettingsSectionView()
    {
        InitializeComponent();
    }
}
