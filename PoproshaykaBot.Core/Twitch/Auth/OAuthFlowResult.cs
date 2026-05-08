namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed record OAuthFlowResult(
    string AccessToken,
    string RefreshToken,
    string[] Scopes,
    string Login,
    string UserId,
    int ExpiresInSeconds);
