using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record YouTubeAuthorizationRefreshed(YouTubeOAuthRole Role) : EventBase;
