namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal static class InnerTubeEndpoints
{
    public const string HttpClientName = "YouTubeInnerTubeStateless";

    public const string WebClientName = "WEB";

    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public const string AcceptLanguage = "en-US,en;q=0.9";

    public static readonly Uri BaseUri = new("https://www.youtube.com", UriKind.Absolute);

    public static string BuildWatchPath(string videoId) =>
        $"/watch?v={Uri.EscapeDataString(videoId)}";

    public static string BuildGetLiveChatPath(string apiKey) =>
        $"/youtubei/v1/live_chat/get_live_chat?key={Uri.EscapeDataString(apiKey)}&prettyPrint=false";
}
