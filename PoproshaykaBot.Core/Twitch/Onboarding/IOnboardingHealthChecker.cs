namespace PoproshaykaBot.Core.Twitch.Onboarding;

public interface IOnboardingHealthChecker
{
    Task<ChatTestMessageOutcome> SendChatTestMessageAsync(
        string broadcasterUserId,
        string senderUserId,
        string clientId,
        string botAccessToken,
        string message,
        CancellationToken cancellationToken);

    Task<OnboardingChatPreviewStartResult> StartChatPreviewAsync(
        string broadcasterUserId,
        string botUserId,
        string clientId,
        string botAccessToken,
        CancellationToken cancellationToken);
}

public enum ChatTestMessageOutcome
{
    Skipped = 0,
    Sent = 1,
    Forbidden = 2,
    Error = 3,
}

public enum OnboardingChatPreviewStatus
{
    Started = 0,
    EventSubNotConnected = 1,
    Forbidden = 2,
    Error = 3,
}

public sealed record OnboardingChatPreviewStartResult(
    OnboardingChatPreviewStatus Status,
    IAsyncDisposable? Session);
