using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class TwitchChannelsApiAdapter(TwitchAPI twitchApi) : ITwitchChannelsApi
{
    public Task ModifyChannelInformationAsync(
        string broadcasterId,
        ModifyChannelInformationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return twitchApi.Helix.Channels.ModifyChannelInformationAsync(broadcasterId, request);
    }
}
