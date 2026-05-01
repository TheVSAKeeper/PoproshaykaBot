namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public interface IChannelInformationApplier
{
    Task<bool> ApplyAsync(BroadcastProfile profile, CancellationToken cancellationToken);
    Task<bool> ApplyPatchAsync(string? title, string? gameId, string? gameName, CancellationToken cancellationToken);
}
