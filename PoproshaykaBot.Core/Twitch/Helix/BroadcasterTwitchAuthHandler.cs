using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BroadcasterTwitchAuthHandler(
    ITwitchOAuthService oauthService,
    SettingsManager settingsManager,
    AccountsStore accountsStore,
    ILogger<BroadcasterTwitchAuthHandler> logger,
    TwitchChannelAuthorization? channelAuthorization = null)
    : TwitchAuthHandlerBase(oauthService, settingsManager, accountsStore, logger, channelAuthorization)
{
    protected override PlatformAuthRole Role => PlatformAuthRole.Broadcaster;

    protected override string MissingTokenHint =>
        "Авторизуйте стримера в карточке канала на вкладке Twitch.";

    protected override Task<string?> GetTokenAsync(ITwitchOAuthService oauthService, CancellationToken cancellationToken)
    {
        return oauthService.GetAccessTokenAsync(Role, cancellationToken);
    }
}
