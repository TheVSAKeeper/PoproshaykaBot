using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube;

internal sealed record YouTubeSessionState(
    string ChannelLabel,
    string? TargetChannelId,
    string? BotChannelId,
    string? BotChannelTitle,
    string BotContextId = AuthContextIds.Default,
    string BroadcasterContextId = AuthContextIds.Default,
    long Generation = 0);

internal sealed class YouTubeSessionContext
{
    private readonly Lock _sync = new();
    private YouTubeSessionState? _current;

    public event EventHandler? Changed;

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

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _current = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
