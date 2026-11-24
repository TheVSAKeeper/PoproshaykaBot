using KeepShell.Services;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

internal static class PlatformAccountLoginDefaults
{
    public static Dictionary<StreamingPlatform, IPlatformAccountLogin> Build(
        IEnumerable<IPlatformAccountLogin>? accountLogins,
        SettingsManager? settingsManager = null,
        AccountsStore? twitchAccountsStore = null,
        ITwitchOAuthService? twitchOAuth = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        IYouTubeOAuthService? youtubeOAuth = null,
        IShellLauncher? shellLauncher = null,
        TimeProvider? timeProvider = null,
        IEmbeddedTwitchAuthDialog? embeddedTwitchAuth = null,
        IPlatformWebChatLoginDialog? webChatLoginDialog = null,
        IEventBus? eventBus = null,
        IEmbeddedYouTubeAuthDialog? embeddedYouTubeAuth = null)
    {
        var logins = (accountLogins ?? []).ToDictionary(static login => login.Platform);
        var clock = timeProvider ?? TimeProvider.System;

        if (!logins.ContainsKey(StreamingPlatform.Twitch)
            && twitchAccountsStore is not null)
        {
            logins[StreamingPlatform.Twitch] = new TwitchAccountLogin(
                settingsManager,
                twitchAccountsStore,
                twitchOAuth,
                shellLauncher,
                clock,
                embeddedTwitchAuth,
                webChatLoginDialog,
                eventBus);
        }

        if (!logins.ContainsKey(StreamingPlatform.YouTube)
            && youtubeAccountsStore is not null)
        {
            logins[StreamingPlatform.YouTube] = new YouTubeAccountLogin(
                youtubeSettingsStore,
                youtubeAccountsStore,
                youtubeOAuth,
                shellLauncher,
                clock,
                webChatLoginDialog,
                eventBus,
                embeddedYouTubeAuth: embeddedYouTubeAuth);
        }

        return logins;
    }

    public static Dictionary<StreamingPlatform, IPlatformAccountDirectory> BuildDirectories(
        IEnumerable<IPlatformAccountDirectory>? directories,
        params IPlatformAccountDirectory?[] fallbacks)
    {
        return (directories ?? [])
            .Concat(fallbacks.OfType<IPlatformAccountDirectory>())
            .DistinctBy(static directory => directory.Platform)
            .ToDictionary(static directory => directory.Platform);
    }
}
