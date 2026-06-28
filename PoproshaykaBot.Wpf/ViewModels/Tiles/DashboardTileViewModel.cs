using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public abstract partial class DashboardTileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isCollapsed;

    protected DashboardTileViewModel(string title)
    {
        _title = title;
    }

    public ObservableCollection<ToolbarItemViewModel> HeaderActions { get; } = [];

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }
}
