using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure;

public enum EmbeddedYouTubeAuthOutcome
{
    None = 0,
    Completed = 1,
    Canceled = 2,
    Unavailable = 3,
    Failed = 4,
}

public sealed record EmbeddedYouTubeAuthResult(
    EmbeddedYouTubeAuthOutcome Outcome,
    YouTubeOAuthFlowResult? Flow,
    string Message)
{
    public static EmbeddedYouTubeAuthResult Completed(YouTubeOAuthFlowResult flow) =>
        new(EmbeddedYouTubeAuthOutcome.Completed, flow, string.Empty);

    public static EmbeddedYouTubeAuthResult Canceled(string message) =>
        new(EmbeddedYouTubeAuthOutcome.Canceled, null, message);

    public static EmbeddedYouTubeAuthResult Unavailable(string message) =>
        new(EmbeddedYouTubeAuthOutcome.Unavailable, null, message);

    public static EmbeddedYouTubeAuthResult Failed(string message) =>
        new(EmbeddedYouTubeAuthOutcome.Failed, null, message);
}
