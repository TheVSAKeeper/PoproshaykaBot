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

    protected DashboardTileViewModel(
        string typeId,
        string title,
        int? maxWidth = null,
        int? maxHeight = null,
        int minWidth = DefaultMinWidth,
        int minHeight = DefaultMinHeight)
    {
        TypeId = typeId;
        _title = title;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MinWidth = minWidth;
        MinHeight = minHeight;
    }

    public const int DefaultMinWidth = 220;

    public const int DefaultMinHeight = 110;

    public string TypeId { get; }

    public int? MaxWidth { get; }

    public int? MaxHeight { get; }

    public int MinWidth { get; }

    public int MinHeight { get; }

    public virtual bool FillsAvailableSpace => false;

    public virtual bool GrowsWithSpace => false;

    public ObservableCollection<ToolbarItemViewModel> HeaderActions { get; } = [];

    [RelayCommand]
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
    }
}
