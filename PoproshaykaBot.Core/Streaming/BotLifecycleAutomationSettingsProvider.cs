using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Streaming;

public sealed class BotLifecycleAutomationSettingsProvider(SettingsManager settingsManager)
    : IBotLifecycleAutomationSettingsProvider
{
    private static readonly BotLifecycleAutomationSettings DisabledSettings = new();

    public BotLifecycleAutomationSettings Get(StreamingPlatform platform)
    {
        return platform switch
        {
            StreamingPlatform.Twitch => settingsManager.Current.Twitch.BotLifecycleAutomation,
            _ => DisabledSettings,
        };
    }
}
