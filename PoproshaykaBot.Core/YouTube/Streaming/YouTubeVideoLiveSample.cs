namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed record YouTubeVideoLiveSample(
    string VideoId,
    string Title,
    DateTimeOffset? ActualStartTimeUtc,
    DateTimeOffset? ActualEndTimeUtc,
    int? ConcurrentViewers);
