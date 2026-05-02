using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Twitch.Helix;

public sealed class BotTwitchAuthHandler(
    TwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger<BotTwitchAuthHandler> logger)
    : TwitchAuthHandlerBase(oauthService, settingsManager, accountsStore, logger)
{
    protected override TwitchOAuthRole Role => TwitchOAuthRole.Bot;

    protected override string MissingTokenHint =>
        "Авторизуйте бота в настройках Twitch (вкладка «Бот»).";

    protected override Task<string?> GetTokenAsync(TwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }
}
