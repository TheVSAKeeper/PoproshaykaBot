namespace PoproshaykaBot.WinForms.Infrastructure.Events.Moderation;

public sealed record UserRewarded(
    string UserId,
    string UserName,
    ulong AddedMessagesCount,
    string? Channel) : EventBase;
