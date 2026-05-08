namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record BotConnectionStatusUpdated(string Message) : EventBase;
