namespace PoproshaykaBot.Core.Platforms;

public interface IPlatformEnablementSource
{
    StreamingPlatform Platform { get; }

    bool IsEnabledInSettings { get; }

    event Action? Changed;
}
