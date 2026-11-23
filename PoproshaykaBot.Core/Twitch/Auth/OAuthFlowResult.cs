namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed record OAuthFlowResult(
    string AccessToken,
    string RefreshToken,
    string[] Scopes,
    string Login,
    string UserId,
    int ExpiresInSeconds)
{
    public string? BrowserProfileName { get; init; }

    public void ApplyTo(TwitchAccountSettings account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        account.AccessToken = AccessToken;
        account.RefreshToken = RefreshToken;
        account.Login = Login;
        account.BrowserProfileName = BrowserProfileName
            ?? (string.Equals(account.UserId, UserId, StringComparison.Ordinal) ? account.BrowserProfileName : null);
        account.UserId = UserId;
        account.Scopes = [..Scopes];
        account.StoredScopes = [..Scopes];
        account.AccessTokenExpiresAt = ExpiresInSeconds > 0 ? now.AddSeconds(ExpiresInSeconds) : null;
    }
}
