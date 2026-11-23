using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Client;

internal sealed class BotYouTubeAuthHandler(
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    ILogger<BotYouTubeAuthHandler> logger,
    YouTubeAccountsStore? accountsStore = null)
    : YouTubeAuthHttpHandlerBase(authService, logger, accountsStore)
{
    protected override PlatformAuthRole Role => PlatformAuthRole.Bot;
}
