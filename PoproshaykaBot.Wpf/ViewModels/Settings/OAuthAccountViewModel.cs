using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class OAuthAccountViewModel : ObservableObject, IDisposable
{
    private static readonly string[] BotDefaultScopes = [..TwitchScopes.BotRequired];
    private static readonly string[] BroadcasterDefaultScopes = [..TwitchScopes.BroadcasterRequired];

    private readonly TwitchOAuthRole _role;
    private readonly ITwitchOAuthService _oauthService;
    private readonly AccountsStore _accountsStore;
    private readonly Func<OAuthCredentialsSnapshot> _credentialsProvider;

    private TwitchAccountSettings _draft = new();
    private CancellationTokenSource? _authCts;

    [ObservableProperty]
    private string _scopesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccessToken))]
    [NotifyCanExecuteChangedFor(nameof(ValidateTokenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearTokensCommand))]
    private string _accessTokenValue = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefreshToken))]
    [NotifyCanExecuteChangedFor(nameof(RefreshTokenCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearTokensCommand))]
    private string _refreshTokenValue = string.Empty;

    [ObservableProperty]
    private string _loginText = "—";

    [ObservableProperty]
    private string _tokenStatusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _tokenStatusSeverity;

    [ObservableProperty]
    private string _lastRefreshText = "Неизвестно";

    [ObservableProperty]
    private string _authStatusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _authStatusSeverity;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeCommand))]
    private bool _isAuthInProgress;

    public OAuthAccountViewModel(
        TwitchOAuthRole role,
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        Func<OAuthCredentialsSnapshot> credentialsProvider)
    {
        _role = role;
        _oauthService = oauthService;
        _accountsStore = accountsStore;
        _credentialsProvider = credentialsProvider;

        _oauthService.StatusChanged += OnOAuthStatusChanged;
    }

    public event EventHandler? SettingChanged;

    public string HeaderText => _role switch
    {
        TwitchOAuthRole.Broadcaster => "Авторизация стримера",
        _ => "Авторизация бота",
    };

    public string AuthorizeButtonText => _role switch
    {
        TwitchOAuthRole.Broadcaster => "Авторизовать стримера",
        _ => "Авторизовать бота",
    };

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessTokenValue);

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshTokenValue);

    public void Load(TwitchAccountSettings draft)
    {
        _draft = draft;
        ScopesText = string.Join(" ", draft.Scopes);
        LoadTokenInformation();
    }

    public void Save()
    {
        _draft.Scopes = ParseScopes();
    }

    public void Dispose()
    {
        _oauthService.StatusChanged -= OnOAuthStatusChanged;
        _authCts?.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanAuthorize))]
    private async Task AuthorizeAsync()
    {
        var credentials = _credentialsProvider();

        if (!ValidateAuthInputs(credentials))
        {
            return;
        }

        var scopes = ParseScopes();

        if (scopes.Length == 0)
        {
            scopes = GetDefaultScopes();
        }

        var cts = new CancellationTokenSource();
        _authCts = cts;
        IsAuthInProgress = true;
        SetAuthStatus("Авторизация...", StatusSeverity.Info);

        try
        {
            var result = await _oauthService.StartOAuthFlowToDraftAsync(
                _role,
                credentials.ClientId,
                credentials.ClientSecret,
                scopes,
                string.IsNullOrWhiteSpace(credentials.RedirectUri) ? null : credentials.RedirectUri,
                OpenInDefaultBrowser,
                ct: cts.Token);

            if (!ReferenceEquals(_authCts, cts) || string.IsNullOrEmpty(result.AccessToken))
            {
                return;
            }

            ApplyAuthResult(result);
            SetAuthStatus("Авторизация успешна! Не забудьте «Сохранить», чтобы сохранить токены.", StatusSeverity.Success);
            RaiseSettingChanged();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (ReferenceEquals(_authCts, cts))
        {
            SetAuthStatus($"Ошибка: {SafeMessage(exception)}", StatusSeverity.Error);
        }
        catch
        {
        }
        finally
        {
            if (ReferenceEquals(_authCts, cts))
            {
                _authCts = null;
                IsAuthInProgress = false;
            }

            cts.Dispose();
        }
    }

    private bool CanAuthorize()
    {
        return !IsAuthInProgress;
    }

    [RelayCommand]
    private async Task CancelAuthAsync()
    {
        if (_authCts is not { } pending)
        {
            return;
        }

        _authCts = null;

        try
        {
            await pending.CancelAsync();
        }
        catch
        {
        }

        SetAuthStatus("Авторизация отменена", StatusSeverity.Warning);
        IsAuthInProgress = false;
    }

    [RelayCommand(CanExecute = nameof(HasAccessToken))]
    private async Task ValidateTokenAsync()
    {
        var accessToken = _draft.AccessToken;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            SetTokenStatus("Токен отсутствует", StatusSeverity.Error);
            return;
        }

        SetTokenStatus("Проверка...", StatusSeverity.Info);

        try
        {
            var isValid = await _oauthService.IsTokenValidAsync(accessToken);

            SetTokenStatus(
                isValid ? "Действителен" : "Недействителен",
                isValid ? StatusSeverity.Success : StatusSeverity.Error);
        }
        catch (Exception exception)
        {
            SetTokenStatus($"Ошибка: {SafeMessage(exception)}", StatusSeverity.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasRefreshToken))]
    private async Task RefreshTokenAsync()
    {
        var credentials = _credentialsProvider();
        var refreshToken = _draft.RefreshToken;

        if (string.IsNullOrWhiteSpace(credentials.ClientId) || string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            StyledMessageBox.Show(
                "Client ID и Client Secret должны быть настроены для обновления токена.",
                "Настройки отсутствуют",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            StyledMessageBox.Show(
                "Refresh Token отсутствует. Требуется повторная авторизация.",
                "Refresh Token отсутствует",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        SetTokenStatus("Обновление...", StatusSeverity.Info);

        try
        {
            await _oauthService.RefreshTokenAsync(_role, credentials.ClientId, credentials.ClientSecret, refreshToken);

            var refreshedLive = _accountsStore.Load(_role);
            _draft.AccessToken = refreshedLive.AccessToken;
            _draft.RefreshToken = refreshedLive.RefreshToken;
            _draft.Login = refreshedLive.Login;
            _draft.UserId = refreshedLive.UserId;
            _draft.Scopes = refreshedLive.Scopes;
            _draft.AccessTokenExpiresAt = refreshedLive.AccessTokenExpiresAt;

            LoadTokenInformation();
            SetTokenStatus("Обновлён успешно", StatusSeverity.Success);
            LastRefreshText = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

            RaiseSettingChanged();
        }
        catch (Exception exception)
        {
            SetTokenStatus($"Ошибка: {SafeMessage(exception)}", StatusSeverity.Error);

            StyledMessageBox.Show(
                $"Не удалось обновить токен: {SafeMessage(exception)}",
                "Ошибка обновления токена",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearTokens))]
    private void ClearTokens()
    {
        var roleName = _role == TwitchOAuthRole.Broadcaster ? "стримера" : "бота";

        var result = StyledMessageBox.Show(
            $"Вы уверены, что хотите очистить токены {roleName}?\n\nИзменения вступят в силу после нажатия «Сохранить».",
            "Подтверждение очистки токенов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _draft.AccessToken = string.Empty;
        _draft.RefreshToken = string.Empty;
        _draft.Login = string.Empty;
        _draft.UserId = string.Empty;
        _draft.AccessTokenExpiresAt = null;

        LoadTokenInformation();
        SetTokenStatus("Токены очищены (применятся после «Сохранить»)", StatusSeverity.Warning);
        LastRefreshText = "Неизвестно";

        RaiseSettingChanged();
    }

    private bool CanClearTokens()
    {
        return HasAccessToken || HasRefreshToken;
    }

    [RelayCommand]
    private void ResetScopes()
    {
        ScopesText = string.Join(" ", GetDefaultScopes());
        RaiseSettingChanged();
    }

    private void ApplyAuthResult(OAuthFlowResult result)
    {
        _draft.AccessToken = result.AccessToken;
        _draft.RefreshToken = result.RefreshToken;
        _draft.Login = result.Login;
        _draft.UserId = result.UserId;
        _draft.Scopes = result.Scopes;
        _draft.AccessTokenExpiresAt = result.ExpiresInSeconds > 0
            ? DateTimeOffset.UtcNow.AddSeconds(result.ExpiresInSeconds)
            : null;

        ScopesText = string.Join(" ", _draft.Scopes);
        LoadTokenInformation();
    }

    private void LoadTokenInformation()
    {
        AccessTokenValue = _draft.AccessToken;
        RefreshTokenValue = _draft.RefreshToken;

        LoginText = string.IsNullOrWhiteSpace(_draft.Login) ? "—" : _draft.Login;

        if (string.IsNullOrWhiteSpace(_draft.AccessToken))
        {
            SetTokenStatus("Токен отсутствует", StatusSeverity.Error);
        }
        else
        {
            SetTokenStatus("Не проверен", StatusSeverity.None);
        }
    }

    private bool ValidateAuthInputs(OAuthCredentialsSnapshot credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.ClientId))
        {
            SetAuthStatus("Введите Client ID", StatusSeverity.Error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(credentials.ClientSecret))
        {
            SetAuthStatus("Введите Client Secret", StatusSeverity.Error);
            return false;
        }

        if (_role == TwitchOAuthRole.Broadcaster && string.IsNullOrWhiteSpace(credentials.Channel))
        {
            SetAuthStatus("Не задан Channel в настройках Twitch", StatusSeverity.Error);
            return false;
        }

        return true;
    }

    private string[] ParseScopes()
    {
        return ScopesText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private string[] GetDefaultScopes()
    {
        return _role == TwitchOAuthRole.Broadcaster ? BroadcasterDefaultScopes : BotDefaultScopes;
    }

    private void OnOAuthStatusChanged(TwitchOAuthRole role, string message)
    {
        if (role != _role)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            SetAuthStatus(message, StatusSeverity.Info);
            return;
        }

        dispatcher.BeginInvoke(() => SetAuthStatus(message, StatusSeverity.Info), DispatcherPriority.Normal);
    }

    private void SetAuthStatus(string text, StatusSeverity severity)
    {
        AuthStatusText = text;
        AuthStatusSeverity = severity;
    }

    private void SetTokenStatus(string text, StatusSeverity severity)
    {
        TokenStatusText = text;
        TokenStatusSeverity = severity;
    }

    private void RaiseSettingChanged()
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OpenInDefaultBrowser(string authUrl)
    {
        // TODO: embedded WebView2 login dialog is Step 5 (onboarding); settings flow stays on the system browser

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true,
        });
    }

    private static string SafeMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "операция отменена",
            TimeoutException => "превышено время ожидания",
            _ => "не удалось выполнить операцию, проверьте настройки и подключение",
        };
    }
}
