using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    public DashboardViewModel(IEnumerable<DashboardTileViewModel> tiles)
    {
        Tiles = [.. tiles];
    }

    public ObservableCollection<DashboardTileViewModel> Tiles { get; }

    public bool HasTiles => Tiles.Count > 0;
}
