using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed record EmbeddedYouTubeAuthRequest(
    PlatformAuthRole Role,
    string ClientId,
    string ClientSecret,
    string[] Scopes,
    bool IsCombined = false);
