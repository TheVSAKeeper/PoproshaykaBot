namespace PoproshaykaBot.Core.YouTube.Streaming;

public interface IYouTubeBroadcastSelectionStore
{
    event Action? SelectionChanged;

    string? GetSelectedVideoId(string channelId);

    void SelectBroadcast(string channelId, string? videoId);
}
