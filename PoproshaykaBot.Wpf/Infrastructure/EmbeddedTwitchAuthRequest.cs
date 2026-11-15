using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed record EmbeddedTwitchAuthRequest(
    PlatformAuthRole Role,
    string ClientId,
    string ClientSecret,
    string[] Scopes,
    string? RedirectUri,
    bool CheckBroadcasterChannel);
