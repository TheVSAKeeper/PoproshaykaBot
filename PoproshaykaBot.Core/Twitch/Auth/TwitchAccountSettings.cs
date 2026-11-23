using PoproshaykaBot.Core.Platforms.Auth;

using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class TwitchAccountSettings : IRoleAccountSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public string[] StoredScopes { get; set; } = [];

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BrowserProfileName { get; set; }
}
