namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed record YouTubePublicLiveVideo(
    string VideoId,
    string ChannelId,
    string ChannelTitle,
    string Title,
    DateTimeOffset? StartedAtUtc);
