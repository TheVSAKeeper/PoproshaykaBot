namespace PoproshaykaBot.WinForms.Settings;

public static class LiveStateMerger
{
    public static void Apply(AppSettings draft, AppSettings live)
    {
        ApplyAccount(live.Twitch.BotAccount, draft.Twitch.BotAccount);
        ApplyAccount(live.Twitch.BroadcasterAccount, draft.Twitch.BroadcasterAccount);

        draft.Twitch.BroadcastProfiles = live.Twitch.BroadcastProfiles;
        draft.Twitch.Polls.Profiles = live.Twitch.Polls.Profiles;
        draft.Twitch.Infrastructure.RecentCategories = live.Twitch.Infrastructure.RecentCategories;
    }

    private static void ApplyAccount(TwitchAccountSettings source, TwitchAccountSettings target)
    {
        target.AccessToken = source.AccessToken;
        target.RefreshToken = source.RefreshToken;
        target.Login = source.Login;
        target.UserId = source.UserId;
        target.StoredScopes = source.StoredScopes;
        target.AccessTokenExpiresAt = source.AccessTokenExpiresAt;
    }
}
