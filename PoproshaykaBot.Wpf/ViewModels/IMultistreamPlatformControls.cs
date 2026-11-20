using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels;

public interface IMultistreamPlatformControls
{
    StreamingPlatform Platform { get; }

    bool RequiresCardSetup { get; }

    bool CanEnableFromSettings => true;

    string SetupActionLabel => "Открыть параметры";

    event EventHandler? StateChanged
    {
        add { }
        remove { }
    }
}
