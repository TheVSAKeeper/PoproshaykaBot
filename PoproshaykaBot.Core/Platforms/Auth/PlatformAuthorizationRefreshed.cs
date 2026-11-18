using PoproshaykaBot.Core.Infrastructure.Events;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed record PlatformAuthorizationRefreshed(
    StreamingPlatform Platform,
    PlatformAuthRole Role,
    string ContextId = AuthContextIds.Default) : EventBase;
