using PoproshaykaBot.WinForms.Twitch.Chat;

namespace PoproshaykaBot.WinForms.Settings;

public static class AppSettingsHydrator
{
    public static void ApplyDefaults(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ApplyAccountScopeDefaults(settings.Twitch.BotAccount, TwitchScopes.BotRequired);
        ApplyAccountScopeDefaults(settings.Twitch.BroadcasterAccount, TwitchScopes.BroadcasterRequired);
    }

    private static void ApplyAccountScopeDefaults(TwitchAccountSettings account, IReadOnlyList<string> defaultScopes)
    {
        if (account.Scopes.Length == 0)
        {
            account.Scopes = [..defaultScopes];
        }
    }
}
