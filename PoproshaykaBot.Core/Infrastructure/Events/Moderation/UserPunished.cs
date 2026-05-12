namespace PoproshaykaBot.Core.Infrastructure.Events.Moderation;

public sealed record UserPunished(
    string UserId,
    string UserName,
    ulong RemovedPoints,
    string? Channel) : EventBase;
