namespace PoproshaykaBot.Core.Streaming;

public interface IStreamStatus
{
    StreamStatus CurrentStatus { get; }

    StreamInfo? CurrentStream { get; }

    Task RefreshLiveSnapshotAsync();
}
