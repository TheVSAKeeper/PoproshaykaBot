using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BotTwitchAuthHandler(
    ITwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger<BotTwitchAuthHandler> logger)
    : TwitchAuthHandlerBase(oauthService, settingsManager, accountsStore, logger)
{
    protected override TwitchOAuthRole Role => TwitchOAuthRole.Bot;

    protected override string MissingTokenHint =>
        "Авторизуйте бота в настройках Twitch (вкладка «Бот»).";

    protected override Task<string?> GetTokenAsync(ITwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }
}
