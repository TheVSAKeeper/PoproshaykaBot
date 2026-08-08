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

    protected DashboardTileViewModel(string typeId, string title, int? maxWidth = null, int? maxHeight = null)
    {
        TypeId = typeId;
        _title = title;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
    }

    public string TypeId { get; }

    public int? MaxWidth { get; }

    public int? MaxHeight { get; }

    public virtual bool FillsAvailableSpace => false;

    public ObservableCollection<ToolbarItemViewModel> HeaderActions { get; } = [];

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }
}
