namespace PoproshaykaBot.Core.YouTube.Streaming;

internal interface IYouTubeActiveBroadcastSource
{
    YouTubeBroadcastSummary? CurrentBroadcast { get; }

    Task RefreshLiveSnapshotAsync(CancellationToken cancellationToken);
}
