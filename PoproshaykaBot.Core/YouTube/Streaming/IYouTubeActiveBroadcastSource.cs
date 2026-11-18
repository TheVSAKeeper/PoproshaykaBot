using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal interface IYouTubeActiveBroadcastSource
{
    YouTubeBroadcastSummary? CurrentBroadcast { get; }

    YouTubeBroadcastSummary? GetActiveBroadcast(ChannelKey channel) => CurrentBroadcast;

    Task RefreshLiveSnapshotAsync(CancellationToken cancellationToken);
}
