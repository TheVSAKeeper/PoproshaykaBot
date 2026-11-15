using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthStatusReporter(ILogger<YouTubeOAuthStatusReporter> logger)
    : PlatformAuthStatusReporter(logger, "YouTube");
