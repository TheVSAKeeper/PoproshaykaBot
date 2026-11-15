namespace PoproshaykaBot.Core.YouTube.Streaming;

public sealed record YouTubeBroadcastSummary(
    string VideoId,
    string ChannelId,
    string ChannelTitle,
    string LiveChatId,
    string Title,
    DateTimeOffset StartedAtUtc);
