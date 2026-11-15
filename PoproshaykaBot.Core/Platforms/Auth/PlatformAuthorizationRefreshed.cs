using PoproshaykaBot.Core.Infrastructure.Events;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed record PlatformAuthorizationRefreshed(StreamingPlatform Platform, PlatformAuthRole Role) : EventBase;
