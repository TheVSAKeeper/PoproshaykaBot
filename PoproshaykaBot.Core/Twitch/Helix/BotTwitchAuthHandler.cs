using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BotTwitchAuthHandler(
    ITwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger<BotTwitchAuthHandler> logger,
    TwitchChannelAuthorization? channelAuthorization = null)
    : TwitchAuthHandlerBase(oauthService, settingsManager, accountsStore, logger, channelAuthorization)
{
    protected override PlatformAuthRole Role => PlatformAuthRole.Bot;

    protected override string MissingTokenHint =>
        "Авторизуйте бота в карточке канала на вкладке Twitch.";

    protected override Task<string?> GetTokenAsync(ITwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }
}
