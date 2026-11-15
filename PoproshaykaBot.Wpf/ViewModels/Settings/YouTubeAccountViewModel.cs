using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class YouTubeAccountViewModel : ObservableObject, IDisposable
{
    private readonly YouTubeOAuthRole _role;
    private readonly IYouTubeOAuthService _oauthService;
    private readonly Func<string> _getClientId;
    private readonly Func<string> _getClientSecret;
    private readonly IShellLauncher _shellLauncher;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<YouTubeAccountViewModel> _logger;

    private YouTubeAccountSettings _draft = new();
    private CancellationTokenSource? _authCts;
    private bool _suppressUpdates;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToken))]
    [NotifyPropertyChangedFor(nameof(HasAnyCredential))]
    [NotifyPropertyChangedFor(nameof(AuthorizationSummary))]
    [NotifyPropertyChangedFor(nameof(OpenBrowserButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _accessTokenValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyCredential))]
    [NotifyPropertyChangedFor(nameof(AuthorizationSummary))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _refreshTokenValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannelIdentity))]
    [NotifyPropertyChangedFor(nameof(ChannelDisplay))]
    private string _channelId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChannelIdentity))]
    [NotifyPropertyChangedFor(nameof(ChannelDisplay))]
    [NotifyPropertyChangedFor(nameof(AuthorizationSummary))]
    private string _channelTitle = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBrowserCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAuthorizationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetScopesCommand))]
    private bool _isAuthorizing;

    [ObservableProperty]
    private string _expectedScopesSummary = string.Empty;

    [ObservableProperty]
    private string _grantedScopesSummary = string.Empty;

    [ObservableProperty]
    private bool _hasScopeMismatch;

    [ObservableProperty]
    private string _tokenExpirySummary = "Нет активного токена";

    public YouTubeAccountViewModel(
        YouTubeOAuthRole role,
        IYouTubeOAuthService oauthService,
        Func<string> getClientId,
        Func<string> getClientSecret,
        IShellLauncher shellLauncher,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        ILogger<YouTubeAccountViewModel> logger)
    {
        _role = role;
        _oauthService = oauthService;
        _getClientId = getClientId;
        _getClientSecret = getClientSecret;
        _shellLauncher = shellLauncher;
        _dialogService = dialogService;
        _uiDispatcher = uiDispatcher;
        _logger = logger;

        _oauthService.StatusChanged += OnServiceStatusChanged;
    }

    public event EventHandler? SettingChanged;

    public bool HasToken => !string.IsNullOrWhiteSpace(AccessTokenValue);

    public bool HasAnyCredential =>
        !string.IsNullOrWhiteSpace(AccessTokenValue) || !string.IsNullOrWhiteSpace(RefreshTokenValue);

    public bool HasChannelIdentity =>
        !string.IsNullOrWhiteSpace(ChannelId) || !string.IsNullOrWhiteSpace(ChannelTitle);

    public string ChannelDisplay =>
        string.IsNullOrWhiteSpace(ChannelTitle)
            ? ChannelId
            : string.IsNullOrWhiteSpace(ChannelId)
                ? ChannelTitle
                : $"{ChannelTitle} ({ChannelId})";

    public string OpenBrowserButtonText => HasToken ? "Переавторизоваться" : "Войти через браузер";

    public string RolePurposeHint => _role switch
    {
        YouTubeOAuthRole.Bot => "Требует право youtube.force-ssl (чтение и отправка сообщений в чат трансляции).",
        YouTubeOAuthRole.Broadcaster => "Требует право youtube.readonly (чтение параметров канала и активной трансляции).",
        _ => string.Empty,
    };

    public string AuthorizationSummary
    {
        get
        {
            if (HasAnyCredential)
            {
                var label = string.IsNullOrWhiteSpace(ChannelTitle) ? "Токены сохранены" : $"Авторизован: {ChannelTitle}";
                return string.IsNullOrWhiteSpace(RefreshTokenValue)
                    ? $"{label} (без refresh-токена)"
                    : label;
            }

            return "Не авторизован";
        }
    }

    public void Load(YouTubeAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        _suppressUpdates = true;
        try
        {
            _draft = JsonStoreClone.DeepClone(account);
            if (_draft.Scopes.Length == 0)
            {
                _draft.Scopes = [..YouTubeScopes.RequiredFor(_role)];
            }

            AccessTokenValue = _draft.AccessToken;
            RefreshTokenValue = _draft.RefreshToken;
            ChannelId = _draft.ChannelId;
            ChannelTitle = _draft.ChannelTitle;
            StatusMessage = string.Empty;
            RefreshDiagnostics();
        }
        finally
        {
            _suppressUpdates = false;
        }
    }

    public void SaveTo(YouTubeAccountSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.AccessToken = AccessTokenValue;
        target.RefreshToken = RefreshTokenValue;
        target.ChannelId = ChannelId;
        target.ChannelTitle = ChannelTitle;
        target.Scopes = [.._draft.Scopes];
        target.StoredScopes = [.._draft.StoredScopes];
        target.AccessTokenExpiresAt = _draft.AccessTokenExpiresAt;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _oauthService.StatusChanged -= OnServiceStatusChanged;
        _authCts?.Cancel();
        _authCts?.Dispose();
    }

    partial void OnAccessTokenValueChanged(string value)
    {
        if (!_suppressUpdates)
        {
            _draft.AccessToken = value;
            RaiseSettingChanged();
        }
    }

    partial void OnRefreshTokenValueChanged(string value)
    {
        if (!_suppressUpdates)
        {
            _draft.RefreshToken = value;
            RaiseSettingChanged();
        }
    }

    private bool CanStartBrowserAuth() => !IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanStartBrowserAuth))]
    private async Task OpenBrowserAsync()
    {
        var clientId = _getClientId().Trim();
        var clientSecret = _getClientSecret().Trim();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            _dialogService.Warning("Настройки YouTube", "Сначала заполните Client ID Desktop-клиента в блоке «Google-проект».");
            return;
        }

        _authCts?.Dispose();
        _authCts = new CancellationTokenSource();
        IsAuthorizing = true;
        StatusMessage = "Открывается системный браузер для входа в Google...";

        try
        {
            var result = await _oauthService.StartOAuthFlowToDraftAsync(
                _role,
                clientId,
                clientSecret,
                _draft.Scopes,
                OpenInSystemBrowser,
                _authCts.Token);

            ApplyFlowResult(result);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Авторизация отменена";
        }
        catch (TimeoutException exception)
        {
            StatusMessage = exception.Message;
            _dialogService.Warning("Вход в YouTube", exception.Message);
        }
        catch (YouTubeOAuthException exception)
        {
            StatusMessage = exception.Message;
            _dialogService.Warning("Ошибка авторизации YouTube", exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка браузерной авторизации YouTube для роли {Role}", _role);
            StatusMessage = $"Ошибка: {exception.Message}";
            _dialogService.Error("Ошибка авторизации YouTube", $"Не удалось завершить вход в Google.\n\n{exception.Message}");
        }
        finally
        {
            IsAuthorizing = false;
            _authCts?.Dispose();
            _authCts = null;
        }
    }

    private bool CanCancelAuthorization() => IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanCancelAuthorization))]
    private void CancelAuthorization()
    {
        _authCts?.Cancel();
        StatusMessage = "Отмена авторизации...";
    }

    private bool CanClear() => !IsAuthorizing && (HasAnyCredential || HasChannelIdentity);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _suppressUpdates = true;
        try
        {
            AccessTokenValue = string.Empty;
            RefreshTokenValue = string.Empty;
            ChannelId = string.Empty;
            ChannelTitle = string.Empty;
            _draft.AccessToken = string.Empty;
            _draft.RefreshToken = string.Empty;
            _draft.ChannelId = string.Empty;
            _draft.ChannelTitle = string.Empty;
            _draft.StoredScopes = [];
            _draft.AccessTokenExpiresAt = null;
            StatusMessage = "Токены очищены. Нажмите «Сохранить», чтобы записать на диск.";
            RefreshDiagnostics();
        }
        finally
        {
            _suppressUpdates = false;
        }

        RaiseSettingChanged();
    }

    private bool CanResetScopes() => !IsAuthorizing;

    [RelayCommand(CanExecute = nameof(CanResetScopes))]
    private void ResetScopes()
    {
        _draft.Scopes = [..YouTubeScopes.RequiredFor(_role)];
        RefreshDiagnostics();
        StatusMessage = "Набор запрашиваемых прав сброшен к базовому.";
        RaiseSettingChanged();
    }

    private void ApplyFlowResult(YouTubeOAuthFlowResult result)
    {
        _suppressUpdates = true;
        try
        {
            result.ApplyTo(_draft, DateTimeOffset.UtcNow);
            AccessTokenValue = _draft.AccessToken;
            RefreshTokenValue = _draft.RefreshToken;
            ChannelId = _draft.ChannelId;
            ChannelTitle = _draft.ChannelTitle;
            RefreshDiagnostics();

            StatusMessage = string.IsNullOrWhiteSpace(result.RefreshToken)
                ? "Вход выполнен без refresh-токена (Google не выдал его повторно). Сохраните настройки."
                : $"Вход выполнен для канала {result.ChannelTitle}. Нажмите «Сохранить», чтобы применить.";
        }
        finally
        {
            _suppressUpdates = false;
        }

        RaiseSettingChanged();
    }

    private void RefreshDiagnostics()
    {
        var expected = _draft.Scopes.Length > 0 ? _draft.Scopes : [..YouTubeScopes.RequiredFor(_role)];
        ExpectedScopesSummary = string.Join(", ", expected.Select(ShortenScopeName));
        GrantedScopesSummary = _draft.StoredScopes.Length > 0
            ? string.Join(", ", _draft.StoredScopes.Select(ShortenScopeName))
            : "–";

        HasScopeMismatch = _draft.StoredScopes.Length > 0 && !YouTubeScopes.SetEquals(expected, _draft.StoredScopes);

        if (string.IsNullOrWhiteSpace(AccessTokenValue))
        {
            TokenExpirySummary = string.IsNullOrWhiteSpace(RefreshTokenValue)
                ? "Нет активного токена"
                : "Access-токен не загружен (обновится по refresh-токену)";
        }
        else if (_draft.AccessTokenExpiresAt is { } expiresAt)
        {
            TokenExpirySummary = $"Действует до {expiresAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        }
        else
        {
            TokenExpirySummary = "Срок жизни access-токена не указан";
        }
    }

    private static string ShortenScopeName(string scope)
    {
        const string Prefix = "https://www.googleapis.com/auth/";
        return scope.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? scope[Prefix.Length..]
            : scope;
    }

    private void OnServiceStatusChanged(YouTubeOAuthRole role, string status)
    {
        if (role != _role || _disposed)
        {
            return;
        }

        _ = _uiDispatcher.InvokeAsync(() => StatusMessage = status);
    }

    private void RaiseSettingChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenInSystemBrowser(string authUrl)
    {
        if (!_shellLauncher.Open(authUrl))
        {
            _logger.LogWarning("Не удалось открыть системный браузер для YouTube OAuth роли {Role}", _role);
        }
    }
}
