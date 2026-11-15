namespace PoproshaykaBot.Core.YouTube;

internal sealed record YouTubeSessionState(
    string ChannelLabel,
    string? TargetChannelId,
    string? BotChannelId,
    string? BotChannelTitle);

internal sealed class YouTubeSessionContext
{
    private readonly Lock _sync = new();
    private YouTubeSessionState? _current;

    public YouTubeSessionState? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public void Begin(YouTubeSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_sync)
        {
            _current = state;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }
    }
}
