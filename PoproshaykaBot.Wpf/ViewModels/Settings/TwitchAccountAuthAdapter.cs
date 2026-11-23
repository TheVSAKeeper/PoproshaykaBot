using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class TwitchAccountAuthAdapter :
    IPlatformAccountAuthAdapter,
    IEditableScopesAccountAuthAdapter,
    IEmbeddedAccountAuthAdapter,
    ITokenMaintenanceAccountAuthAdapter
{
    private static readonly string[] BotDefaultScopes = [..TwitchScopes.BotRequired, ..TwitchScopes.BotOptional];
    private static readonly string[] BroadcasterDefaultScopes = [..TwitchScopes.BroadcasterRequired];

    private static readonly PlatformAccountDisplayOptions TwitchDisplayOptions = new(
        ShowsRolePurposeHint: false,
        ShowsAuthorizationSummary: false,
        ShowsLoginAndRefreshMetadata: true,
        ShowsChannelIdentity: false,
        ShowsScopeSummaries: false);

    private readonly PlatformAuthRole _role;
    private readonly ITwitchOAuthService _oauthService;
    private readonly AccountsStore _accountsStore;
    private readonly Func<OAuthCredentialsSnapshot> _credentialsProvider;
    private readonly IEmbeddedTwitchAuthDialog _embeddedAuth;

    private TwitchAccountSettings _draft = new();

    public TwitchAccountAuthAdapter(
        PlatformAuthRole role,
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        Func<OAuthCredentialsSnapshot> credentialsProvider,
        IEmbeddedTwitchAuthDialog embeddedAuth)
    {
        ArgumentNullException.ThrowIfNull(oauthService);
        ArgumentNullException.ThrowIfNull(accountsStore);
        ArgumentNullException.ThrowIfNull(credentialsProvider);
        ArgumentNullException.ThrowIfNull(embeddedAuth);

        _role = role;
        _oauthService = oauthService;
        _accountsStore = accountsStore;
        _credentialsProvider = credentialsProvider;
        _embeddedAuth = embeddedAuth;
    }

    public string PlatformKey => "Twitch";

    public PlatformAuthRole Role => _role;

    public PlatformAccountDisplayOptions DisplayOptions => TwitchDisplayOptions;

    public string RolePurposeHint => string.Empty;

    public string EmbeddedAuthorizeButtonText => _role switch
    {
        PlatformAuthRole.Broadcaster => "Авторизовать стримера в приложении",
        _ => "Авторизовать бота в приложении",
    };

    public string OpenBrowserButtonText(bool hasToken) => _role switch
    {
        PlatformAuthRole.Broadcaster => "Авторизовать стримера в браузере",
        _ => "Авторизовать бота в браузере",
    };

    public string ErrorDialogTitle => "Ошибка авторизации Twitch";

    public string TimeoutDialogTitle => "Вход в Twitch";

    public string BrowserErrorDialogHeader => "Не удалось завершить вход в Twitch.";

    public string ClearConfirmationTitle => "Подтверждение очистки токенов";

    public string ClearConfirmationMessage
    {
        get
        {
            var roleName = _role == PlatformAuthRole.Broadcaster ? "стримера" : "бота";
            return $"Вы уверены, что хотите очистить токены {roleName}?\n\nИзменения вступят в силу после нажатия «Сохранить».";
        }
    }

    public string ClearStatusMessage => "Токены очищены (применятся после «Сохранить»)";

    public event Action<PlatformAuthRole, string>? StatusChanged
    {
        add => _oauthService.StatusChanged += value;
        remove => _oauthService.StatusChanged -= value;
    }

    public void Load(IRoleAccountSettings account)
    {
        if (account is not TwitchAccountSettings twitchAccount)
        {
            throw new ArgumentException("Ожидались настройки аккаунта Twitch.", nameof(account));
        }

        _draft = JsonStoreClone.DeepClone(twitchAccount);
    }

    public void SaveTo(IRoleAccountSettings target)
    {
        if (target is not TwitchAccountSettings twitchTarget)
        {
            throw new ArgumentException("Ожидались настройки аккаунта Twitch.", nameof(target));
        }

        twitchTarget.AccessToken = _draft.AccessToken;
        twitchTarget.RefreshToken = _draft.RefreshToken;
        twitchTarget.Login = _draft.Login;
        twitchTarget.UserId = _draft.UserId;
        twitchTarget.BrowserProfileName = _draft.BrowserProfileName;
        twitchTarget.Scopes = [.._draft.Scopes];
        twitchTarget.StoredScopes = [.._draft.StoredScopes];
        twitchTarget.AccessTokenExpiresAt = _draft.AccessTokenExpiresAt;
    }

    public string ReadAccessToken() => _draft.AccessToken;

    public string ReadRefreshToken() => _draft.RefreshToken;

    public string ReadScopesText() => string.Join(" ", _draft.Scopes);

    public void WriteScopesText(string scopesText)
    {
        _draft.Scopes = ParseScopes(scopesText);
    }

    public string ResetScopes()
    {
        var defaults = GetDefaultScopes();
        _draft.Scopes = [..defaults];
        return string.Join(" ", defaults);
    }

    public bool HasAdditionalStateToClear() => false;

    public void OnManualTokenEdited(string accessToken, string refreshToken)
    {
        _draft.AccessToken = accessToken;
        _draft.RefreshToken = refreshToken;
        _draft.Login = string.Empty;
        _draft.UserId = string.Empty;
        _draft.BrowserProfileName = null;
        _draft.AccessTokenExpiresAt = null;
        _draft.StoredScopes = [];
    }

    public void ClearDraft()
    {
        _draft.AccessToken = string.Empty;
        _draft.RefreshToken = string.Empty;
        _draft.Login = string.Empty;
        _draft.UserId = string.Empty;
        _draft.BrowserProfileName = null;
        _draft.AccessTokenExpiresAt = null;
        _draft.StoredScopes = [];
    }

    public bool TryPrepareBrowserAuthorization(
        out string? statusError,
        out (string Title, string Message)? warningDialog)
    {
        warningDialog = null;
        return TryValidateInputs(_credentialsProvider(), out statusError);
    }

    public async Task<PlatformAuthFlowOutcome> StartBrowserAuthorizationAsync(
        string scopesText,
        Action<string> openUrl,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var credentials = _credentialsProvider();
        var scopes = GetRequestedScopes(scopesText);
        var redirectUri = string.IsNullOrWhiteSpace(credentials.RedirectUri) ? null : credentials.RedirectUri;

        var result = await _oauthService.StartOAuthFlowToDraftAsync(
            _role,
            credentials.ClientId,
            credentials.ClientSecret,
            scopes,
            redirectUri,
            openUrl,
            checkBroadcasterChannel: true,
            ct);

        result.ApplyTo(_draft, now);

        return new(
            Succeeded: true,
            IsWarning: false,
            StatusMessage: "Авторизация успешна! Нажмите «Сохранить» для сохранения токенов.",
            UpdatedScopesText: string.Join(" ", _draft.Scopes),
            RaiseSettingChanged: true);
    }

    public bool TryPrepareEmbeddedAuthorization(out string? statusError)
    {
        return TryValidateInputs(_credentialsProvider(), out statusError);
    }

    public async Task<PlatformAuthFlowOutcome> StartEmbeddedAuthorizationAsync(
        string scopesText,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var credentials = _credentialsProvider();
        var scopes = GetRequestedScopes(scopesText);

        var request = new EmbeddedTwitchAuthRequest(
            _role,
            credentials.ClientId,
            credentials.ClientSecret,
            scopes,
            string.IsNullOrWhiteSpace(credentials.RedirectUri) ? null : credentials.RedirectUri,
            true);

        var result = await _embeddedAuth.AuthorizeAsync(request);

        if (result is { Outcome: EmbeddedTwitchAuthOutcome.Completed, Flow: { } flow }
            && !string.IsNullOrEmpty(flow.AccessToken))
        {
            flow.ApplyTo(_draft, now);
            return new(
                Succeeded: true,
                IsWarning: false,
                StatusMessage: "Авторизация успешна! Не забудьте «Сохранить», чтобы сохранить токены.",
                UpdatedScopesText: string.Join(" ", _draft.Scopes),
                RaiseSettingChanged: true);
        }

        return new(
            Succeeded: false,
            IsWarning: result.Outcome != EmbeddedTwitchAuthOutcome.Failed,
            StatusMessage: result.Message);
    }

    public bool TryPrepareTokenRefresh(
        out (string Title, string Message)? warningDialog,
        out string refreshToken)
    {
        var credentials = _credentialsProvider();
        refreshToken = _draft.RefreshToken;

        if (string.IsNullOrWhiteSpace(credentials.ClientId) || string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            warningDialog = ("Настройки отсутствуют", "Client ID и Client Secret должны быть настроены для обновления токена.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            warningDialog = ("Refresh Token отсутствует", "Refresh Token отсутствует. Требуется повторная авторизация.");
            return false;
        }

        warningDialog = null;
        return true;
    }

    public async Task<PlatformTokenCheckOutcome> ValidateTokenAsync(string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new("Токен отсутствует", StatusSeverity.Error);
        }

        var isValid = await _oauthService.IsTokenValidAsync(accessToken, ct);

        return new(
            isValid ? "Действителен" : "Недействителен",
            isValid ? StatusSeverity.Success : StatusSeverity.Error);
    }

    public async Task<PlatformTokenRefreshOutcome> RefreshTokenAsync(
        string refreshToken,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var credentials = _credentialsProvider();
        await _oauthService.RefreshTokenAsync(_role, credentials.ClientId, credentials.ClientSecret, refreshToken, ct);

        var refreshedLive = _accountsStore.Load(_role);
        _draft.AccessToken = refreshedLive.AccessToken;
        _draft.RefreshToken = refreshedLive.RefreshToken;
        _draft.Login = refreshedLive.Login;
        _draft.UserId = refreshedLive.UserId;
        _draft.BrowserProfileName = refreshedLive.BrowserProfileName;
        _draft.Scopes = [..refreshedLive.Scopes];
        _draft.StoredScopes = [..refreshedLive.StoredScopes];
        _draft.AccessTokenExpiresAt = refreshedLive.AccessTokenExpiresAt;

        return new(
            Succeeded: true,
            TokenStatusText: "Обновлён успешно",
            TokenStatusSeverity: StatusSeverity.Success,
            AccessToken: _draft.AccessToken,
            RefreshToken: _draft.RefreshToken);
    }

    public string? FormatSafeError(Exception exception) => null;

    public PlatformAccountPresentation BuildPresentation(DateTimeOffset now)
    {
        var login = string.IsNullOrWhiteSpace(_draft.Login) ? "–" : _draft.Login;
        var (tokenStatus, tokenSeverity) = ResolveTokenStatus(now);

        return new(
            AuthorizationSummary: string.Empty,
            LoginText: login,
            TokenStatusText: tokenStatus,
            TokenStatusSeverity: tokenSeverity,
            ChannelId: string.Empty,
            ChannelTitle: string.Empty,
            ChannelDisplay: string.Empty,
            HasChannelIdentity: false,
            ExpectedScopesSummary: string.Empty,
            GrantedScopesSummary: string.Empty,
            HasScopeMismatch: false,
            TokenExpirySummary: string.Empty);
    }

    private (string Text, StatusSeverity Severity) ResolveTokenStatus(DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_draft.AccessToken))
        {
            return ("Токен отсутствует", StatusSeverity.Error);
        }

        if (_draft.AccessTokenExpiresAt is not { } expiresAt)
        {
            return ("Не проверен", StatusSeverity.None);
        }

        var expiresAtLocal = TimeZoneInfo.ConvertTime(expiresAt, TimeZoneInfo.Local)
            .ToString("dd.MM.yyyy HH:mm", UiCulture.Russian);

        return expiresAt > now
            ? ($"действует до {expiresAtLocal}", StatusSeverity.None)
            : ($"истёк {expiresAtLocal}", StatusSeverity.Warning);
    }

    private bool TryValidateInputs(OAuthCredentialsSnapshot credentials, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(credentials.ClientId))
        {
            errorMessage = "Введите Client ID";
            return false;
        }

        if (string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            errorMessage = "Введите Client Secret";
            return false;
        }

        if (_role == PlatformAuthRole.Broadcaster && string.IsNullOrWhiteSpace(credentials.Channel))
        {
            errorMessage = "Не задан Channel в настройках Twitch";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private string[] GetDefaultScopes()
    {
        return _role == PlatformAuthRole.Broadcaster ? BroadcasterDefaultScopes : BotDefaultScopes;
    }

    private string[] GetRequestedScopes(string scopesText)
    {
        var required = _role == PlatformAuthRole.Broadcaster
            ? TwitchScopes.BroadcasterRequired
            : TwitchScopes.BotRequired;

        return ParseScopes(scopesText).Union(required, StringComparer.Ordinal).ToArray();
    }

    private static string[] ParseScopes(string scopesText)
    {
        return scopesText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
