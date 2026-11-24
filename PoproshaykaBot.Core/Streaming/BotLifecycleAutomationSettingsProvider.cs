using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Streaming;

public sealed class BotLifecycleAutomationSettingsProvider(PlatformLifecycleSettingsStore store)
    : IBotLifecycleAutomationSettingsProvider
{
    public BotLifecycleAutomationSettings Get(StreamingPlatform platform)
    {
        return store.Get(platform);
    }
}
