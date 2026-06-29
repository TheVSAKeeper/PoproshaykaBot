using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BroadcasterHelixClient(IHttpClientFactory httpClientFactory, ILogger<BroadcasterHelixClient> logger)
    : TwitchHelixClient(httpClientFactory, logger)
{
    protected override string HttpClientName => TwitchEndpoints.HelixBroadcasterClient;
}
