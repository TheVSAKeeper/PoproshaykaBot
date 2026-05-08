using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Twitch.Helix;

public sealed class BotHelixClient(IHttpClientFactory httpClientFactory, ILogger<TwitchHelixClient> logger)
    : TwitchHelixClient(httpClientFactory, logger)
{
    protected override string HttpClientName => TwitchEndpoints.HelixBotClient;
}
