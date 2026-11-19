namespace PoproshaykaBot.Core.YouTube.Streaming;

internal interface IYouTubePublicLiveDiscoveryClient
{
    Task<YouTubePublicLiveVideo?> FindActiveLiveVideoAsync(
        string channelId,
        CancellationToken cancellationToken);
}
