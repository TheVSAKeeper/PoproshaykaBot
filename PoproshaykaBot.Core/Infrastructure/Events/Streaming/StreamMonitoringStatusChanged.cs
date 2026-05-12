using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record StreamMonitoringStatusChanged(TwitchOAuthRole Role, StreamMonitoringStatus Status, string? Detail = null) : EventBase;
