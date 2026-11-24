using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.YouTube.Streaming;

public sealed record YouTubeBroadcastUpdateRequest(
    string BroadcastId,
    string Title,
    string? Description,
    DateTimeOffset? ScheduledStartTimeUtc,
    string? PrivacyStatus);

public sealed record YouTubeBroadcastUpdateResult(
    bool IsSuccess,
    YouTubeApiErrorKind ErrorKind = YouTubeApiErrorKind.None,
    string? ErrorMessage = null)
{
    public static YouTubeBroadcastUpdateResult Success() => new(true);

    public static YouTubeBroadcastUpdateResult Failure(YouTubeApiErrorKind errorKind, string errorMessage) =>
        new(false, errorKind, errorMessage);
}

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

    Task<IReadOnlyList<YouTubeBroadcastSummary>> ListActiveBroadcastsAsync(
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken);

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

    Task<YouTubeBroadcastUpdateResult> UpdateBroadcastMetadataAsync(
        YouTubeBroadcastUpdateRequest request,
        string? contextId,
        CancellationToken cancellationToken);

    Task<YouTubeBroadcastUpdateResult> UpdateBroadcastMetadataAsync(
        YouTubeBroadcastUpdateRequest request,
        PlatformAuthRole role,
        string? contextId,
        CancellationToken cancellationToken);
}
