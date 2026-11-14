using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserRewarded(
    PlatformUserKey User,
    string UserName,
    ulong AddedPoints,
    string? Channel) : EventBase, IPlatformEvent
{
    public StreamingPlatform Platform => User.Platform;
}
