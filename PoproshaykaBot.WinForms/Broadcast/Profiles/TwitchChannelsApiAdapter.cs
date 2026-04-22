using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class TwitchChannelsApiAdapter(ITwitchHelixClient helix) : ITwitchChannelsApi
{
    public Task ModifyChannelInformationAsync(
        string broadcasterId,
        PatchChannelRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return helix.PatchChannelAsync(broadcasterId, request, cancellationToken);
    }
}
