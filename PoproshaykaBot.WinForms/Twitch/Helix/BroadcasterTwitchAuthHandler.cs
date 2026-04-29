using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class BroadcasterTwitchAuthHandler(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
    ILogger<BroadcasterTwitchAuthHandler> logger)
    : TwitchAuthHandlerBase(oauthService, settingsManager, logger)
{
    protected override TwitchOAuthRole Role => TwitchOAuthRole.Broadcaster;

    protected override string MissingTokenHint =>
        "Авторизуйте стримера в настройках Twitch (вкладка «Стример»).";

    protected override Task<string?> GetTokenAsync(TwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }

    protected override TwitchAccountSettings GetAccount(TwitchSettings twitch)
    {
        return twitch.BroadcasterAccount;
    }
}
