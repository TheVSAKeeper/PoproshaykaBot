using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record TwitchAuthorizationRefreshed(TwitchOAuthRole Role) : EventBase;
