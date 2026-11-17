using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserPunished(
    PlatformUserKey User,
    string UserName,
    ulong RemovedPoints,
    string? Channel,
    ChannelKey? ChannelKey = null) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => User.Platform;
}
