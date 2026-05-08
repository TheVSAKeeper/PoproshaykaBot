namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record ChannelUpdated(
    string Title,
    string Language,
    string GameId,
    string GameName,
    IReadOnlyList<string> ContentClassificationLabels) : EventBase;
