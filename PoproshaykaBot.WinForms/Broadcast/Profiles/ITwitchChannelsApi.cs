using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

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
