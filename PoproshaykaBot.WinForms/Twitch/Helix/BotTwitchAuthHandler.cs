using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class BotTwitchAuthHandler(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
    ILogger<BotTwitchAuthHandler> logger)
    : TwitchAuthHandlerBase(oauthService, settingsManager, logger)
{
    protected override TwitchOAuthRole Role => TwitchOAuthRole.Bot;

    protected override string MissingTokenHint =>
        "Авторизуйте бота в настройках Twitch (вкладка «Бот»).";

    protected override Task<string?> GetTokenAsync(TwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }

    protected override TwitchAccountSettings GetAccount(TwitchSettings twitch)
    {
        return twitch.BotAccount;
    }
}
