namespace PoproshaykaBot.Core.YouTube.Streaming;

internal interface IYouTubeLiveBroadcastClient
{
    Task<string?> ResolveChannelIdByHandleAsync(string handle, CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(CancellationToken cancellationToken);

    Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, CancellationToken cancellationToken);
}
