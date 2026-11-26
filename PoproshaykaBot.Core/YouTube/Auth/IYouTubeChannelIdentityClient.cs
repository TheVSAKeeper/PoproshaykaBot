namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed record YouTubeChannelIdentity(
    string ChannelId,
    string ChannelTitle);

public interface IYouTubeChannelIdentityClient
{
    Task<YouTubeChannelIdentity> GetOwnChannelAsync(string accessToken, CancellationToken ct = default);

    Task<YouTubeChannelIdentity?> ResolveChannelByHandleAsync(
        string handle,
        string accessToken,
        CancellationToken ct = default) => Task.FromResult<YouTubeChannelIdentity?>(null);
}
