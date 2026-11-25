using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.Infrastructure;

internal static class BroadcastChannelSelection
{
    public static string? Resolve(IBroadcastState state, IChatPlatformRegistry registry)
    {
        var configured = registry.All.Where(platform => !string.IsNullOrWhiteSpace(platform.ConfiguredChannel)).ToArray();

        return (configured.FirstOrDefault(platform => state.IsPlatformLive(platform.Id))
            ?? configured.FirstOrDefault(platform => platform.IsEnabled)
            ?? (configured.Length > 0 ? configured[0] : null))?.ConfiguredChannel;
    }
}
