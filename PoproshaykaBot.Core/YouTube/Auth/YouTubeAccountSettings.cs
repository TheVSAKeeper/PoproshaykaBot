using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeAccountSettings : IRoleAccountSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public string ChannelTitle { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public string[] StoredScopes { get; set; } = [];

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
}
