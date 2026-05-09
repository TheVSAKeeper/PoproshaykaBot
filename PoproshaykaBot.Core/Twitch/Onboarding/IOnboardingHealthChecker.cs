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

    Task<ModeratorCheckOutcome> CheckBotIsModeratorAsync(
        string broadcasterUserId,
        string botUserId,
        string clientId,
        string broadcasterAccessToken,
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

public enum ModeratorCheckOutcome
{
    Skipped = 0,
    IsModerator = 1,
    NotModerator = 2,
    Error = 3,
    MissingScope = 4,
    OwnsChannel = 5,
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
