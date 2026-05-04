namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface IChannelUpdateConfirmation
{
    Task<bool> AwaitAsync(string? expectedTitle, string? expectedGameId, TimeSpan timeout, CancellationToken cancellationToken);
}
