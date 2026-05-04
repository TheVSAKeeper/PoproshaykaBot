using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface ITwitchChannelsApi
{
    Task ModifyChannelInformationAsync(
        string broadcasterId,
        PatchChannelRequest request,
        CancellationToken cancellationToken);

    Task<ChannelInfo?> GetChannelInformationAsync(
        string broadcasterId,
        CancellationToken cancellationToken);
}
