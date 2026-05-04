namespace PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;

public sealed record BroadcastSchedulerStateChanged(
    bool IsActive,
    string? Channel,
    int SentMessagesCount,
    DateTime? NextBroadcastTime) : EventBase;
