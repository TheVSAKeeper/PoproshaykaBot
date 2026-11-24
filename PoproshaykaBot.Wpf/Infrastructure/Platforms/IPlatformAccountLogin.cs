using KeepShell.ViewModels;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public interface IPlatformAccountLogin
{
    StreamingPlatform Platform { get; }

    bool CanAuthorize { get; }

    bool SupportsEmbeddedBrowser { get; }

    bool SupportsTokenValidation => false;

    bool SupportsEditableScopes => false;

    bool SupportsWebChatSignIn => WebChatDescriptor is not null;

    PlatformWebChatLoginDescriptor? WebChatDescriptor => null;

    PlatformOAuthMethodDescriptor OAuthMethod => new(
        SupportsEmbeddedBrowser ? "Встроенный или системный браузер" : "Системный браузер",
        null,
        SupportsEmbeddedBrowser,
        SupportsSystemBrowser: true);

    IReadOnlyList<string> GetDefaultScopes(PlatformAuthRole role) => [];

    string[] MergeRequestedScopes(PlatformAuthRole role, IReadOnlyCollection<string> configuredScopes) =>
        [.. configuredScopes];

    bool IsScopeSatisfied(IReadOnlySet<string> grantedScopes, string requiredScope) =>
        grantedScopes.Contains(requiredScope);

    bool TryEvaluateScopeMismatch(
        PlatformAuthRole role,
        string[] expectedScopes,
        string[] storedScopes,
        out string[] effectiveExpectedScopes)
    {
        effectiveExpectedScopes = expectedScopes;
        return storedScopes.Length > 0
            && !new HashSet<string>(storedScopes, StringComparer.Ordinal).SetEquals(expectedScopes);
    }

    bool ShouldTreatScopeMismatchAsSessionFailure(PlatformAuthRole role, IReadOnlySet<string> grantedScopes) => true;

    Task<PlatformAccountLoginResult> AuthorizeAsync(PlatformAccountLoginRequest request);

    Task<PlatformTokenValidationResult> ValidateTokenAsync(
        PlatformAuthRole role,
        string accessToken,
        CancellationToken ct = default) =>
        Task.FromResult(new PlatformTokenValidationResult("Проверка токена не поддерживается.", StatusSeverity.Info, false));

    Task<PlatformTokenRefreshResult> RefreshTokenAsync(
        PlatformAuthRole role,
        string contextId,
        string refreshToken,
        CancellationToken ct = default) =>
        Task.FromResult(new PlatformTokenRefreshResult(false, false, "Обновление токена не поддерживается.", StatusSeverity.Info));

    PlatformManualEditResult SaveManualEdits(
        PlatformAuthRole role,
        string contextId,
        PlatformManualTokenEdits edits) =>
        new(false, false, false, "Ручное редактирование не поддерживается.", StatusSeverity.Info);

    Task<PlatformWebChatSignInResult> SignInWebChatAsync(string accountId, string accountLabel) =>
        Task.FromResult(new PlatformWebChatSignInResult(
            PlatformWebChatLoginOutcome.Unavailable,
            null,
            false,
            "Вход в веб-чат не поддерживается.",
            StatusSeverity.Info));

    void ResetRetryState(PlatformAuthRole role, string contextId)
    {
    }
}

public sealed record PlatformOAuthMethodDescriptor(
    string MethodTitle,
    string? HintText,
    bool SupportsEmbeddedBrowser,
    bool SupportsSystemBrowser);

public sealed record PlatformAccountLoginRequest(
    IReadOnlyList<PlatformAuthRole> Roles,
    string? AccountId,
    string? ContextId,
    IReadOnlyList<string> Scopes,
    bool UseSystemBrowser,
    bool ForceDefaultContext = false,
    bool PreferDefaultContextWhenEmpty = true);

public sealed record PlatformAccountLoginResult(
    string Message,
    StatusSeverity Severity,
    IReadOnlyList<AuthContextSummary> ChangedAuthorizations,
    ResolvedChannelIdentity? OwnChannel = null);

public sealed record PlatformTokenValidationResult(
    string Message,
    StatusSeverity Severity,
    bool IsValid);

public sealed record PlatformTokenRefreshResult(
    bool Succeeded,
    bool Persisted,
    string Message,
    StatusSeverity Severity);

public sealed record PlatformManualTokenEdits(
    bool AccessTokenEdited,
    string AccessToken,
    string InitialAccessToken,
    bool RefreshTokenEdited,
    string RefreshToken,
    string InitialRefreshToken,
    bool ScopesEdited,
    string ScopesText);

public sealed record PlatformManualEditResult(
    bool Applied,
    bool Written,
    bool Conflict,
    string Message,
    StatusSeverity Severity);

public sealed record PlatformWebChatSignInResult(
    PlatformWebChatLoginOutcome Outcome,
    string? BrowserProfileName,
    bool Persisted,
    string? Message,
    StatusSeverity Severity);
