namespace PoproshaykaBot.Core.Streaming;

public interface IStreamStatus
{
    StreamStatus CurrentStatus { get; }

    StreamInfo? CurrentStream { get; }

    StreamStatusSnapshot GetSnapshot();

    bool RequiresBackgroundPoll => false;

    Task RefreshLiveSnapshotAsync();
}
