using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BroadcasterTwitchAuthHandler(
    ITwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger<BroadcasterTwitchAuthHandler> logger)
    : TwitchAuthHandlerBase(oauthService, settingsManager, accountsStore, logger)
{
    protected override TwitchOAuthRole Role => TwitchOAuthRole.Broadcaster;

    protected override string MissingTokenHint =>
        "Авторизуйте стримера в настройках Twitch (вкладка «Стример»).";

    protected override Task<string?> GetTokenAsync(ITwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }
}
