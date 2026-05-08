namespace PoproshaykaBot.Core.Server;

public sealed record SseChannelOptions(
    int GlobalChannelCapacity = 512,
    int ClientChannelCapacity = 256,
    int DropLogThrottle = 50,
    int DropNotifyThreshold = 10);
