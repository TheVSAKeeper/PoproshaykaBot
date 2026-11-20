using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

public interface IMultistreamPlatformControls
{
    StreamingPlatform Platform { get; }

    bool RequiresCardSetup { get; }
}
