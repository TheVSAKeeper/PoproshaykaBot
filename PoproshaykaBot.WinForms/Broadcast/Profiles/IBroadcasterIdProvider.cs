namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public interface IBroadcasterIdProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}
