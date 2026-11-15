namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class InMemoryYouTubeBroadcastSelectionStore : IYouTubeBroadcastSelectionStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, string> _selected = new(StringComparer.Ordinal);

    public event Action? SelectionChanged;

    public string? GetSelectedVideoId(string channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        lock (_sync)
        {
            return _selected.GetValueOrDefault(channelId);
        }
    }

    public void SelectBroadcast(string channelId, string? videoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(videoId))
            {
                _selected.Remove(channelId);
            }
            else
            {
                _selected[channelId] = videoId.Trim();
            }
        }

        SelectionChanged?.Invoke();
    }
}
