namespace PoproshaykaBot.WinForms.Auth;

public sealed class TwitchAccountSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];

    public string[] StoredScopes { get; set; } = [];

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
}
