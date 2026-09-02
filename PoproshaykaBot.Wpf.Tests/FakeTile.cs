using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests;

internal sealed class FakeTile(string typeId, bool fills = false, bool grows = false, int? maxWidth = null, int? maxHeight = null)
    : DashboardTileViewModel(typeId, typeId, maxWidth, maxHeight)
{
    public override bool FillsAvailableSpace => fills;

    public override bool GrowsWithSpace => grows;
}
