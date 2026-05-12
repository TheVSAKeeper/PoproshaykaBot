namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserRewarded(
    string UserId,
    string UserName,
    ulong AddedPoints,
    string? Channel) : EventBase;
