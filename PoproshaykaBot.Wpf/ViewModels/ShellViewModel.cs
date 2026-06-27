using MahApps.Metro.IconPacks;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class ShellViewModel : ShellViewModelBase
{
    public ShellViewModel(ModalHostViewModel modal, SpikePageViewModel overview)
        : base(modal)
    {
        Sections.Add(new NavigationItem("Обзор", PackIconLucideKind.LayoutDashboard, overview));
        Selected = Sections[0];
    }

    protected override void NavigateToSettings()
    {
        Selected = Sections[0];
    }
}
