using System.Collections.Frozen;

namespace PoproshaykaBot.Core.Platforms;

public static class PlatformDisplayNames
{
    private static readonly FrozenDictionary<StreamingPlatform, string> Names =
        new Dictionary<StreamingPlatform, string>
        {
            [StreamingPlatform.Twitch] = "Twitch",
            [StreamingPlatform.YouTube] = "YouTube",
            [StreamingPlatform.VkLive] = "VK Live",
        }.ToFrozenDictionary();

    public static string Get(StreamingPlatform platform) =>
        Names.GetValueOrDefault(platform) ?? platform.ToString();
}
