namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed record YouTubeOAuthFlowResult(
    string AccessToken,
    string RefreshToken,
    IReadOnlyList<string> Scopes,
    string ChannelId,
    string ChannelTitle,
    int ExpiresInSeconds)
{
    public string? BrowserProfileName { get; init; }

    public void ApplyTo(YouTubeAccountSettings account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);

        account.AccessToken = AccessToken;
        account.RefreshToken = RefreshToken;
        account.BrowserProfileName = BrowserProfileName
            ?? (string.Equals(account.ChannelId, ChannelId, StringComparison.Ordinal) ? account.BrowserProfileName : null);
        account.ChannelId = ChannelId;
        account.ChannelTitle = ChannelTitle;
        account.Scopes = [..Scopes];
        account.StoredScopes = [..Scopes];
        account.AccessTokenExpiresAt = ExpiresInSeconds > 0 ? now.AddSeconds(ExpiresInSeconds) : null;
    }
}
