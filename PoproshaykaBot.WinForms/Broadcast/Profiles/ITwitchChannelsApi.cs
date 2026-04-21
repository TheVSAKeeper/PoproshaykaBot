using TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public interface ITwitchChannelsApi
{
    Task ModifyChannelInformationAsync(
        string broadcasterId,
        ModifyChannelInformationRequest request,
        CancellationToken cancellationToken);
}
