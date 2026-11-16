using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public interface IUserLinkSource
{
    event EventHandler<PlatformUserKey>? UserRequested;
}
