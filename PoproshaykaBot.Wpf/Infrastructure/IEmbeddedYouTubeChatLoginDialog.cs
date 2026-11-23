namespace PoproshaykaBot.Wpf.Infrastructure;

public enum EmbeddedYouTubeChatLoginOutcome
{
    None = 0,
    Completed = 1,
    Canceled = 2,
    Unavailable = 3,
    Failed = 4,
}

public sealed record EmbeddedYouTubeChatLoginRequest(string AccountId, string AccountLabel);

public sealed record EmbeddedYouTubeChatLoginResult(
    EmbeddedYouTubeChatLoginOutcome Outcome,
    string? BrowserProfileName = null,
    string? Message = null)
{
    public static EmbeddedYouTubeChatLoginResult Completed(string browserProfileName) =>
        new(EmbeddedYouTubeChatLoginOutcome.Completed, browserProfileName);

    public static EmbeddedYouTubeChatLoginResult Canceled() =>
        new(EmbeddedYouTubeChatLoginOutcome.Canceled);

    public static EmbeddedYouTubeChatLoginResult Unavailable(string message) =>
        new(EmbeddedYouTubeChatLoginOutcome.Unavailable, Message: message);

    public static EmbeddedYouTubeChatLoginResult Failed(string message) =>
        new(EmbeddedYouTubeChatLoginOutcome.Failed, Message: message);
}

public interface IEmbeddedYouTubeChatLoginDialog
{
    Task<EmbeddedYouTubeChatLoginResult> SignInAsync(EmbeddedYouTubeChatLoginRequest request);
}
