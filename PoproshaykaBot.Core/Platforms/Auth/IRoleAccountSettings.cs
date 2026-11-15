namespace PoproshaykaBot.Core.Platforms.Auth;

public interface IRoleAccountSettings
{
    string AccessToken { get; set; }

    string RefreshToken { get; set; }

    string[] Scopes { get; set; }

    string[] StoredScopes { get; set; }

    DateTimeOffset? AccessTokenExpiresAt { get; set; }
}
