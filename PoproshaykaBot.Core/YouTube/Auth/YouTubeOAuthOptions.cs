namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthOptions
{
    public const string HttpClientName = "YouTubeOAuth";

    public string AuthorizationEndpoint { get; init; } = "https://accounts.google.com/o/oauth2/v2/auth";

    public string TokenEndpoint { get; init; } = "https://oauth2.googleapis.com/token";

    public string ChannelsEndpoint { get; init; } = "https://www.googleapis.com/youtube/v3/channels";

    public TimeSpan AuthTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan LoopbackConnectionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CredentialsRejectedCooldown { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan TokenRefreshMargin { get; init; } = TimeSpan.FromMinutes(5);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
