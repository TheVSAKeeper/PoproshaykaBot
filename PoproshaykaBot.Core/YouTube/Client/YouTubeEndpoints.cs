namespace PoproshaykaBot.Core.YouTube.Client;

internal static class YouTubeEndpoints
{
    public const string BotApiHttpClient = "YouTubeBotApi";
    public const string BroadcasterApiHttpClient = "YouTubeBroadcasterApi";

    public static readonly Uri RestApiBaseUri = new("https://www.googleapis.com/");
    public static readonly Uri GrpcApiBaseUri = new("https://youtube.googleapis.com");
}
