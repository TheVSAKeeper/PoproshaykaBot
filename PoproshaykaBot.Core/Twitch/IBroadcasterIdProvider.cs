namespace PoproshaykaBot.Core.Twitch;

public interface IBroadcasterIdProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}
