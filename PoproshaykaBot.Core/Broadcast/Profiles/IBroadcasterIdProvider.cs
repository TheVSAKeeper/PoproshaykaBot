namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface IBroadcasterIdProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}
