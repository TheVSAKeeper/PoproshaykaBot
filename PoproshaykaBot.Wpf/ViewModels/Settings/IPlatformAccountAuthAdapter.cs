using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record PlatformAccountDisplayOptions(
    bool ShowsRolePurposeHint,
    bool ShowsAuthorizationSummary,
    bool ShowsLoginAndRefreshMetadata,
    bool ShowsChannelIdentity,
    bool ShowsScopeSummaries,
    string ChannelIdentityLabel = "");

public sealed record PlatformAccountAuthCapabilities(
    bool SupportsEditableScopes,
    bool SupportsEmbeddedAuth,
    bool SupportsTokenValidation,
    bool SupportsManualTokenRefresh,
    bool ShowsRolePurposeHint,
    bool ShowsAuthorizationSummary,
    bool ShowsLoginAndRefreshMetadata,
    bool ShowsChannelIdentity,
    bool ShowsScopeSummaries,
    string ChannelIdentityLabel = "");

public sealed record PlatformAccountPresentation(
    string AuthorizationSummary,
    string LoginText,
    string TokenStatusText,
    StatusSeverity TokenStatusSeverity,
    string ChannelId,
    string ChannelTitle,
    string ChannelDisplay,
    bool HasChannelIdentity,
    string ExpectedScopesSummary,
    string GrantedScopesSummary,
    bool HasScopeMismatch,
    string TokenExpirySummary);

public sealed record PlatformAuthFlowOutcome(
    bool Succeeded,
    bool IsWarning,
    string StatusMessage,
    string? UpdatedScopesText = null,
    bool RaiseSettingChanged = false);

public sealed record PlatformTokenCheckOutcome(
    string StatusText,
    StatusSeverity Severity);

public sealed record PlatformTokenRefreshOutcome(
    bool Succeeded,
    string TokenStatusText,
    StatusSeverity TokenStatusSeverity,
    string AccessToken,
    string RefreshToken);

public interface IEditableScopesAccountAuthAdapter
{
    void WriteScopesText(string scopesText);
}

public interface IEmbeddedAccountAuthAdapter
{
    bool SupportsEmbeddedAuth => true;

    string EmbeddedAuthorizeButtonText { get; }

    bool TryPrepareEmbeddedAuthorization(out string? statusError);

    Task<PlatformAuthFlowOutcome> StartEmbeddedAuthorizationAsync(
        string scopesText,
        DateTimeOffset now,
        CancellationToken ct);
}

public interface ITokenMaintenanceAccountAuthAdapter
{
    bool TryPrepareTokenRefresh(
        out (string Title, string Message)? warningDialog,
        out string refreshToken);

    Task<PlatformTokenCheckOutcome> ValidateTokenAsync(string accessToken, CancellationToken ct);

    Task<PlatformTokenRefreshOutcome> RefreshTokenAsync(
        string refreshToken,
        DateTimeOffset now,
        CancellationToken ct);
}

public interface IPlatformAccountAuthAdapter
{
    string PlatformKey { get; }

    PlatformAuthRole Role { get; }

    PlatformAccountDisplayOptions DisplayOptions { get; }

    string RolePurposeHint { get; }

    string OpenBrowserButtonText(bool hasToken);

    string ErrorDialogTitle { get; }

    string TimeoutDialogTitle { get; }

    string BrowserErrorDialogHeader { get; }

    string ClearConfirmationTitle { get; }

    string ClearConfirmationMessage { get; }

    string ClearStatusMessage { get; }

    event Action<PlatformAuthRole, string>? StatusChanged;

    void Load(IRoleAccountSettings account);

    void SaveTo(IRoleAccountSettings target);

    string ReadAccessToken();

    string ReadRefreshToken();

    string ReadScopesText();

    string ResetScopes();

    bool HasAdditionalStateToClear();

    void OnManualTokenEdited(string accessToken, string refreshToken);

    void ClearDraft();

    bool TryPrepareBrowserAuthorization(
        out string? statusError,
        out (string Title, string Message)? warningDialog);

    Task<PlatformAuthFlowOutcome> StartBrowserAuthorizationAsync(
        string scopesText,
        Action<string> openUrl,
        DateTimeOffset now,
        CancellationToken ct);

    string? FormatSafeError(Exception exception);

    PlatformAccountPresentation BuildPresentation(DateTimeOffset now);
}
