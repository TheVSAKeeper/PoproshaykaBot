using PoproshaykaBot.WinForms.Auth;

namespace PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;

public sealed record TwitchAuthorizationRefreshed(TwitchOAuthRole Role) : EventBase;
