using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class TwitchChannelsApiAdapter(
    [FromKeyedServices(TwitchEndpoints.HelixBroadcasterClient)]
    ITwitchHelixClient helix)
    : ITwitchChannelsApi
{
    public Task ModifyChannelInformationAsync(
        string broadcasterId,
        PatchChannelRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return helix.PatchChannelAsync(broadcasterId, request, cancellationToken);
    }

    public Task<ChannelInfo?> GetChannelInformationAsync(
        string broadcasterId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return helix.GetChannelInfoAsync(broadcasterId, cancellationToken);
    }
}
