using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal interface IYouTubeLiveBroadcastClient
{
    Task<string?> ResolveChannelIdByHandleAsync(string handle, CancellationToken cancellationToken);

    Task<string?> ResolveChannelIdByHandleAsync(
        string handle,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        return ResolveChannelIdByHandleAsync(handle, cancellationToken);
    }

    Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(string? contextId, CancellationToken cancellationToken)
    {
        return ListActiveBroadcastsAsync(cancellationToken);
    }

    Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, CancellationToken cancellationToken);

    Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(string videoId, string? contextId, CancellationToken cancellationToken)
    {
        return GetVideoLiveSampleAsync(videoId, cancellationToken);
    }

    Task<YouTubeVideoLiveSample?> GetVideoLiveSampleAsync(
        string videoId,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken)
    {
        return GetVideoLiveSampleAsync(videoId, contextId, cancellationToken);
    }
}
