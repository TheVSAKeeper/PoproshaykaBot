namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public interface IChannelInformationApplier
{
    Task ApplyAsync(BroadcastProfile profile, CancellationToken cancellationToken);
    Task ApplyPatchAsync(string? title, string? gameId, string? gameName, CancellationToken cancellationToken);
}
