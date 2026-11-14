using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Streaming;

public interface IBotLifecycleAutomationSettingsProvider
{
    BotLifecycleAutomationSettings Get(StreamingPlatform platform);
}
