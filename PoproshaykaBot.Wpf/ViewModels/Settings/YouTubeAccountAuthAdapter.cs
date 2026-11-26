using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class YouTubeAccountAuthAdapter : IPlatformAccountAuthAdapter, IEmbeddedAccountAuthAdapter
{
    private static readonly PlatformAccountDisplayOptions YouTubeDisplayOptions = new(
        ShowsRolePurposeHint: true,
        ShowsAuthorizationSummary: true,
        ShowsLoginAndRefreshMetadata: false,
        ShowsChannelIdentity: true,
        ShowsScopeSummaries: true,
        ChannelIdentityLabel: "Канал Google-аккаунта: ");

    private readonly PlatformAuthRole _role;
    private readonly IYouTubeOAuthService _oauthService;
    private readonly Func<string> _getClientId;
    private readonly Func<string> _getClientSecret;
    private readonly IEmbeddedYouTubeAuthDialog? _embeddedAuth;

    private YouTubeAccountSettings _draft = new();

    public YouTubeAccountAuthAdapter(
        PlatformAuthRole role,
        IYouTubeOAuthService oauthService,
        Func<string> getClientId,
        Func<string> getClientSecret,
        IEmbeddedYouTubeAuthDialog? embeddedAuth = null)
    {
        ArgumentNullException.ThrowIfNull(oauthService);
        ArgumentNullException.ThrowIfNull(getClientId);
        ArgumentNullException.ThrowIfNull(getClientSecret);

        _role = role;
        _oauthService = oauthService;
        _getClientId = getClientId;
        _getClientSecret = getClientSecret;
        _embeddedAuth = embeddedAuth;
    }

    public bool SupportsEmbeddedAuth => _embeddedAuth is not null;

    public string EmbeddedAuthorizeButtonText => _role switch
    {
        PlatformAuthRole.Broadcaster => "Авторизовать стримера в приложении",
        _ => "Авторизовать бота в приложении",
    };

    public string PlatformKey => "YouTube";

    public PlatformAuthRole Role => _role;

    public PlatformAccountDisplayOptions DisplayOptions => YouTubeDisplayOptions;

    public string RolePurposeHint => _role switch
    {
        PlatformAuthRole.Bot => "Требует право youtube.force-ssl (чтение и отправка сообщений в чат трансляции).",
        PlatformAuthRole.Broadcaster => "Требует право youtube.force-ssl (чтение параметров канала и обновление названия и описания трансляции).",
        _ => string.Empty,
    };

    public string OpenBrowserButtonText(bool hasToken) => hasToken ? "Переавторизоваться" : "Войти через браузер";

    public string ErrorDialogTitle => "Ошибка авторизации YouTube";

    public string TimeoutDialogTitle => "Вход в YouTube";

    public string BrowserErrorDialogHeader => "Не удалось завершить вход в Google.";

    public string ClearConfirmationTitle => "Подтверждение очистки токенов";

    public string ClearConfirmationMessage
    {
        get
        {
            var roleName = _role == PlatformAuthRole.Broadcaster ? "стримера" : "бота";
            return $"Вы уверены, что хотите очистить токены и канал YouTube для роли {roleName}?\n\nИзменения вступят в силу после нажатия «Сохранить».";
        }
    }

    public string ClearStatusMessage => "Токены очищены. Нажмите «Сохранить», чтобы записать на диск.";

    public event Action<PlatformAuthRole, string>? StatusChanged
    {
        add => _oauthService.StatusChanged += value;
        remove => _oauthService.StatusChanged -= value;
    }

    public void Load(IRoleAccountSettings account)
    {
        if (account is not YouTubeAccountSettings youtubeAccount)
        {
            throw new ArgumentException("Ожидались настройки аккаунта YouTube.", nameof(account));
        }

        _draft = JsonStoreClone.DeepClone(youtubeAccount);
        if (_draft.Scopes.Length == 0)
        {
            _draft.Scopes = [..YouTubeScopes.RequiredFor(_role)];
        }
    }

    public void SaveTo(IRoleAccountSettings target)
    {
        if (target is not YouTubeAccountSettings youtubeTarget)
        {
            throw new ArgumentException("Ожидались настройки аккаунта YouTube.", nameof(target));
        }

        youtubeTarget.AccessToken = _draft.AccessToken;
        youtubeTarget.RefreshToken = _draft.RefreshToken;
        youtubeTarget.ChannelId = _draft.ChannelId;
        youtubeTarget.ChannelTitle = _draft.ChannelTitle;
        youtubeTarget.BrowserProfileName = _draft.BrowserProfileName;
        youtubeTarget.Scopes = [.._draft.Scopes];
        youtubeTarget.StoredScopes = [.._draft.StoredScopes];
        youtubeTarget.AccessTokenExpiresAt = _draft.AccessTokenExpiresAt;
    }

    public string ReadAccessToken() => _draft.AccessToken;

    public string ReadRefreshToken() => _draft.RefreshToken;

    public string ReadScopesText() => string.Join(" ", _draft.Scopes);

    public string ResetScopes()
    {
        var required = YouTubeScopes.RequiredFor(_role);
        _draft.Scopes = [..required];
        return string.Join(" ", required);
    }

    public bool HasAdditionalStateToClear()
    {
        return !string.IsNullOrWhiteSpace(_draft.ChannelId) || !string.IsNullOrWhiteSpace(_draft.ChannelTitle);
    }

    public void OnManualTokenEdited(string accessToken, string refreshToken)
    {
        _draft.AccessToken = accessToken;
        _draft.RefreshToken = refreshToken;
        _draft.ChannelId = string.Empty;
        _draft.ChannelTitle = string.Empty;
        _draft.BrowserProfileName = null;
        _draft.AccessTokenExpiresAt = null;
        _draft.StoredScopes = [];
    }

    public void ClearDraft()
    {
        _draft.AccessToken = string.Empty;
        _draft.RefreshToken = string.Empty;
        _draft.ChannelId = string.Empty;
        _draft.ChannelTitle = string.Empty;
        _draft.BrowserProfileName = null;
        _draft.StoredScopes = [];
        _draft.AccessTokenExpiresAt = null;
    }

    public bool TryPrepareBrowserAuthorization(
        out string? statusError,
        out (string Title, string Message)? warningDialog)
    {
        statusError = null;

        if (string.IsNullOrWhiteSpace(_getClientId().Trim()))
        {
            warningDialog = ("Настройки YouTube", "Сначала заполните Client ID Desktop-клиента в блоке «Google-проект».");
            return false;
        }

        warningDialog = null;
        return true;
    }

    public async Task<PlatformAuthFlowOutcome> StartBrowserAuthorizationAsync(
        string scopesText,
        Action<string> openUrl,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var clientId = _getClientId().Trim();
        var clientSecret = _getClientSecret().Trim();

        var result = await _oauthService.StartOAuthFlowToDraftAsync(
            _role,
            clientId,
            clientSecret,
            _draft.Scopes,
            openUrl,
            ct);

        result.ApplyTo(_draft, now);

        var statusMessage = string.IsNullOrWhiteSpace(result.RefreshToken)
            ? "Вход выполнен без refresh-токена (Google не выдал его повторно). Сохраните настройки."
            : $"Вход выполнен для канала {result.ChannelTitle}. Нажмите «Сохранить», чтобы применить.";

        return new(
            Succeeded: true,
            IsWarning: false,
            StatusMessage: statusMessage,
            RaiseSettingChanged: true);
    }

    public bool TryPrepareEmbeddedAuthorization(out string? statusError)
    {
        if (string.IsNullOrWhiteSpace(_getClientId().Trim()))
        {
            statusError = "Сначала заполните Client ID Desktop-клиента в блоке «Google-проект».";
            return false;
        }

        statusError = null;
        return true;
    }

    public async Task<PlatformAuthFlowOutcome> StartEmbeddedAuthorizationAsync(
        string scopesText,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (_embeddedAuth is null)
        {
            return new(false, true, "Встроенный вход YouTube недоступен.");
        }

        var clientId = _getClientId().Trim();
        var clientSecret = _getClientSecret().Trim();
        var request = new EmbeddedYouTubeAuthRequest(_role, clientId, clientSecret, _draft.Scopes);
        var result = await _embeddedAuth.AuthorizeAsync(request, ct);

        if (result is { Outcome: EmbeddedYouTubeAuthOutcome.Completed, Flow: { } flow }
            && !string.IsNullOrEmpty(flow.AccessToken))
        {
            flow.ApplyTo(_draft, now);
            var statusMessage = string.IsNullOrWhiteSpace(flow.RefreshToken)
                ? "Вход выполнен без refresh-токена (Google не выдал его повторно). Сохраните настройки."
                : $"Вход выполнен для канала {flow.ChannelTitle}. Нажмите «Сохранить», чтобы применить.";

            return new(
                Succeeded: true,
                IsWarning: false,
                StatusMessage: statusMessage,
                RaiseSettingChanged: true);
        }

        if (result.Outcome == EmbeddedYouTubeAuthOutcome.SwitchToBrowser)
        {
            return new(
                Succeeded: false,
                IsWarning: false,
                StatusMessage: result.Message,
                SwitchToBrowser: true);
        }

        return new(
            Succeeded: false,
            IsWarning: result.Outcome != EmbeddedYouTubeAuthOutcome.Failed,
            StatusMessage: result.Message);
    }

    public string? FormatSafeError(Exception exception)
    {
        return exception is YouTubeOAuthException oauthException
            ? YouTubeOAuthErrorFormatter.DescribeSafe(oauthException)
            : null;
    }

    public PlatformAccountPresentation BuildPresentation(DateTimeOffset now)
    {
        var channelId = _draft.ChannelId;
        var channelTitle = _draft.ChannelTitle;
        var hasChannelIdentity = !string.IsNullOrWhiteSpace(channelId) || !string.IsNullOrWhiteSpace(channelTitle);

        var channelDisplay = string.IsNullOrWhiteSpace(channelTitle)
            ? channelId
            : string.IsNullOrWhiteSpace(channelId)
                ? channelTitle
                : $"{channelTitle} ({channelId})";

        var expected = _draft.Scopes.Length > 0 ? _draft.Scopes : [..YouTubeScopes.RequiredFor(_role)];
        var expectedSummary = string.Join(", ", expected.Select(ShortenScopeName));
        var grantedSummary = _draft.StoredScopes.Length > 0
            ? string.Join(", ", _draft.StoredScopes.Select(ShortenScopeName))
            : "–";

        var hasScopeMismatch = _draft.StoredScopes.Length > 0
            && (!YouTubeScopes.SetEquals(expected, _draft.StoredScopes)
                || !YouTubeScopes.ContainsRequiredScopes(_role, _draft.StoredScopes));

        var hasAnyCredential = !string.IsNullOrWhiteSpace(_draft.AccessToken)
            || !string.IsNullOrWhiteSpace(_draft.RefreshToken);

        string authorizationSummary;
        if (hasAnyCredential)
        {
            var label = string.IsNullOrWhiteSpace(channelTitle) ? "Токены сохранены" : $"Авторизован: {channelTitle}";
            authorizationSummary = string.IsNullOrWhiteSpace(_draft.RefreshToken)
                ? $"{label} (без refresh-токена)"
                : label;
        }
        else
        {
            authorizationSummary = "Не авторизован";
        }

        string tokenExpirySummary;
        if (string.IsNullOrWhiteSpace(_draft.AccessToken))
        {
            tokenExpirySummary = string.IsNullOrWhiteSpace(_draft.RefreshToken)
                ? "Нет активного токена"
                : "Access-токен не загружен (обновится по refresh-токену)";
        }
        else if (_draft.AccessTokenExpiresAt is { } expiresAt)
        {
            var localTime = TimeZoneInfo.ConvertTime(expiresAt, TimeZoneInfo.Local)
                .ToString("dd.MM.yyyy HH:mm", UiCulture.Russian);
            tokenExpirySummary = expiresAt <= now
                ? $"Истёк {localTime}"
                : $"Действует до {localTime}";
        }
        else
        {
            tokenExpirySummary = "Срок жизни access-токена не указан";
        }

        return new(
            AuthorizationSummary: authorizationSummary,
            LoginText: string.Empty,
            TokenStatusText: string.Empty,
            TokenStatusSeverity: StatusSeverity.None,
            ChannelId: channelId,
            ChannelTitle: channelTitle,
            ChannelDisplay: channelDisplay,
            HasChannelIdentity: hasChannelIdentity,
            ExpectedScopesSummary: expectedSummary,
            GrantedScopesSummary: grantedSummary,
            HasScopeMismatch: hasScopeMismatch,
            TokenExpirySummary: tokenExpirySummary);
    }

    private static string ShortenScopeName(string scope)
    {
        const string prefix = "https://www.googleapis.com/auth/";
        return scope.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? scope[prefix.Length..]
            : scope;
    }
}
