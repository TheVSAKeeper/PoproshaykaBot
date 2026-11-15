using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Client;

internal sealed class BroadcasterYouTubeAuthHandler(
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    ILogger<BroadcasterYouTubeAuthHandler> logger)
    : YouTubeAuthHttpHandlerBase(authService, logger)
{
    protected override PlatformAuthRole Role => PlatformAuthRole.Broadcaster;
}
