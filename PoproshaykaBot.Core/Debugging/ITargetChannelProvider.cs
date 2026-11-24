using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Debugging;

public interface ITargetChannelProvider
{
    StreamingPlatform DebugPlatform => StreamingPlatform.Twitch;

    TargetChannelState Current { get; }

    void BeginSession();

    void EndSession();

    void SetSessionLogin(string login)
    {
    }
}
