using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserRewarded(
    PlatformUserKey User,
    string UserName,
    ulong AddedPoints,
    string? Channel,
    ChannelKey? ChannelKey = null) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => User.Platform;
}
