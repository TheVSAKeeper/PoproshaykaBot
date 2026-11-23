namespace PoproshaykaBot.Core.YouTube.Auth;

internal static class YouTubeOAuthRoleHelpers
{
    public static void ClearAccountInPlace(YouTubeAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        account.AccessToken = string.Empty;
        account.RefreshToken = string.Empty;
        account.ChannelId = string.Empty;
        account.ChannelTitle = string.Empty;
        account.StoredScopes = [];
        account.AccessTokenExpiresAt = null;
        account.BrowserProfileName = null;
    }
}
