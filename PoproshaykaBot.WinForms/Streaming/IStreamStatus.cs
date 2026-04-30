namespace PoproshaykaBot.WinForms.Streaming;

public interface IStreamStatus
{
    StreamStatus CurrentStatus { get; }

    StreamInfo? CurrentStream { get; }

    Task RefreshLiveSnapshotAsync();
}
