using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthStatusReporter(ILogger<OAuthStatusReporter> logger)
    : PlatformAuthStatusReporter(logger, "Twitch");
