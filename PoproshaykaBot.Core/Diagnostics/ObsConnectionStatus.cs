namespace PoproshaykaBot.Core.Diagnostics;

public sealed record ObsConnectionStatus(
    ConnectionState State,
    DateTimeOffset? LastEventAt,
    bool? IsEnabled = null,
    string? ObsWebSocketVersion = null,
    string? ErrorMessage = null);
