using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record ChannelUpdated(
    StreamingPlatform Platform,
    string Title,
    string Language,
    string GameId,
    string GameName,
    IReadOnlyList<string> ContentClassificationLabels) : EventBase, IPlatformEvent;
