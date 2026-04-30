namespace PoproshaykaBot.WinForms.Infrastructure.Events.Moderation;

public sealed record UserPunished(
    string UserId,
    string UserName,
    ulong RemovedMessagesCount,
    string? Channel) : EventBase;
