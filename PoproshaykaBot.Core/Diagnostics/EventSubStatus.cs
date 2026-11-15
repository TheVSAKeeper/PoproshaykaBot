using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Diagnostics;

public sealed record EventSubStatus(
    PlatformAuthRole Role,
    ConnectionState State,
    DateTimeOffset? LastMessageAt,
    int ReconnectAttempt,
    int ReconnectLimit);
