namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamWentOffline(string Channel, bool IsCatchUp = false) : EventBase;
