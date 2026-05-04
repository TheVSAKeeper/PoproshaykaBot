namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record BotJoinedChannel(string Channel) : EventBase;
