using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using System.Net.Http;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public abstract partial class AuthorizationPageViewModelBase : OnboardingPageViewModelBase, IDisposable
{
    private static readonly string[] BotDefaultScopes = [..TwitchScopes.BotRequired, ..TwitchScopes.BotOptional];
    private static readonly string[] BroadcasterDefaultScopes = [..TwitchScopes.BroadcasterRequired];

    private readonly PlatformAuthRole _role;
    private readonly ITwitchOAuthService _oauthService;
    private readonly AccountsStore _accountsStore;
    private readonly ILogger _logger;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClipboardService _clipboard;
    private readonly IEmbeddedTwitchAuthDialog _embeddedAuth;
    private readonly IUiDispatcher _uiDispatcher;

    private OnboardingContext? _context;
    private OnboardingContext? _credentialsSubscriptionContext;
    private CancellationTokenSource? _authCts;
    private string? _currentAuthUrl;
    private bool _statusSubscribed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenBrowserButtonText))]
    [NotifyPropertyChangedFor(nameof(CopyLinkButtonText))]
    [NotifyPropertyChangedFor(nameof(EmbeddedAuthButtonText))]
    [NotifyPropertyChangedFor(nameof(NextButtonTextOverride))]
    [NotifyPropertyChangedFor(nameof(IsNextActionSecondary))]
    private bool _hasToken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenBrowserButtonText))]
    [NotifyPropertyChangedFor(nameof(CopyLinkButtonText))]
    [NotifyPropertyChangedFor(nameof(EmbeddedAuthButtonText))]
    private bool _isAccountFixed;

    [ObservableProperty]
    private string? _expectedAccountLabel;

    [ObservableProperty]
    private string _expectedAccountHint = string.Empty;

    [ObservableProperty]
    private string _broadcasterCapabilitiesHint = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _resultSeverity;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusMessageSeverity;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AuthorizeInAppCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAuthCommand))]
    private bool _isAuthInProgress;

    protected AuthorizationPageViewModelBase(
        PlatformAuthRole role,
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        ILogger logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard,
        IEmbeddedTwitchAuthDialog embeddedAuth,
        IUiDispatcher uiDispatcher)
    {
        _role = role;
        _oauthService = oauthService;
        _accountsStore = accountsStore;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;
        _embeddedAuth = embeddedAuth;
        _uiDispatcher = uiDispatcher;
    }

    public override string PageTitle => _role == PlatformAuthRole.Broadcaster
        ? "Авторизация стримера Twitch"
        : "Авторизация бота Twitch";

    public override string? NextButtonTextOverride =>
        _role == PlatformAuthRole.Broadcaster && !HasToken && CanAdvance
            ? "Продолжить только с ботом"
            : null;

    public override bool IsNextActionSecondary =>
        _role == PlatformAuthRole.Broadcaster && !HasToken && CanAdvance;

    public override bool IsApplicable(OnboardingContext context) => context.Settings.Twitch.Enabled;

    public string IntroText => _role == PlatformAuthRole.Broadcaster
        ? "Авторизация стримера Twitch даёт права на опросы и управление трансляцией. Для режима «только бот» этот шаг можно пропустить."
        : "Авторизуйте аккаунт бота Twitch. От его имени бот будет писать в чат и слушать сообщения.";

    public string OpenBrowserButtonText => HasToken
        ? (IsAccountFixed ? "Обновить через браузер" : "Сменить через браузер")
        : "Открыть в браузере";

    public string CopyLinkButtonText => HasToken
        ? (IsAccountFixed ? "Ссылка для входа" : "Ссылка для смены")
        : "Скопировать ссылку";

    public string EmbeddedAuthButtonText => HasToken
        ? (IsAccountFixed ? "Обновить вход в приложении" : "Сменить в приложении")
        : "Войти в приложении";

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;

        if (!ReferenceEquals(_credentialsSubscriptionContext, context))
        {
            if (_credentialsSubscriptionContext is not null)
            {
                _credentialsSubscriptionContext.CredentialsChanged -= OnCredentialsChanged;
            }

            context.CredentialsChanged += OnCredentialsChanged;
            _credentialsSubscriptionContext = context;
        }

        RefreshFromContext();
    }

    public override void OnNavigatingBack(OnboardingContext context)
    {
        CancelPendingAuthOnLeave();
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        CancelPendingAuthOnLeave();
        return base.OnLeavingAsync(context);
    }

    public override bool TryHandleEscape()
    {
        if (!IsAuthInProgress || _authCts is not { } pending)
        {
            return false;
        }

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeStatus();

            if (_credentialsSubscriptionContext is not null)
            {
                _credentialsSubscriptionContext.CredentialsChanged -= OnCredentialsChanged;
                _credentialsSubscriptionContext = null;
            }

            _authCts?.Cancel();
            _authCts?.Dispose();
            _authCts = null;
        }
    }

    [RelayCommand]
    private async Task OpenBrowserAsync()
    {
        if (IsAuthInProgress)
        {
            if (_currentAuthUrl is { } pendingUrl)
            {
                OpenInBrowser(pendingUrl);
            }

            return;
        }

        await StartAuthorizationAsync(authUrl =>
        {
            _currentAuthUrl = authUrl;
            OpenInBrowser(authUrl);
        });
    }

    [RelayCommand]
    private async Task CopyLinkAsync()
    {
        if (IsAuthInProgress)
        {
            if (_currentAuthUrl is { } pendingUrl)
            {
                CopyToClipboard(pendingUrl);
            }

            return;
        }

        await StartAuthorizationAsync(authUrl =>
        {
            _currentAuthUrl = authUrl;
            CopyToClipboard(authUrl);
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartEmbeddedAuth))]
    private async Task AuthorizeInAppAsync()
    {
        if (_context is null || _authCts is not null || IsAuthInProgress)
        {
            return;
        }

        var settings = _context.Settings.Twitch;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            ResultText = "Не заполнены Client ID или Secret";
            ResultSeverity = StatusSeverity.Error;
            return;
        }

        var request = new EmbeddedTwitchAuthRequest(
            _role,
            settings.ClientId,
            settings.ClientSecret,
            GetDefaultScopes(),
            string.IsNullOrWhiteSpace(settings.RedirectUri) ? null : settings.RedirectUri,
            CheckBroadcasterChannel: false);

        IsAuthInProgress = true;
        ResultText = string.Empty;
        StatusMessage = "Открываем встроенное окно авторизации...";
        StatusMessageSeverity = StatusSeverity.Info;
        _logger.OAuthEmbeddedFlowStarted(_role, OAuthAuthorizationSurface.OnboardingWizard);

        var completed = false;
        var switchToBrowser = false;

        try
        {
            var result = await _embeddedAuth.AuthorizeAsync(request);

            if (result is { Outcome: EmbeddedTwitchAuthOutcome.Completed, Flow: { } flow })
            {
                completed = ApplyAuthResult(flow);
            }
            else if (result.Outcome == EmbeddedTwitchAuthOutcome.SwitchToBrowser)
            {
                switchToBrowser = true;
            }
            else if (result.Outcome is EmbeddedTwitchAuthOutcome.Canceled or EmbeddedTwitchAuthOutcome.Unavailable)
            {
                StatusMessage = result.Message;
                StatusMessageSeverity = StatusSeverity.Warning;
            }
            else
            {
                ResultText = "Ошибка авторизации";
                ResultSeverity = StatusSeverity.Error;
                StatusMessage = result.Message;
                StatusMessageSeverity = StatusSeverity.Error;
            }
        }
        finally
        {
            IsAuthInProgress = false;
        }

        if (switchToBrowser)
        {
            await OpenBrowserAsync();
            return;
        }

        if (completed)
        {
            RefreshFromContext();
        }
    }

    private bool CanStartEmbeddedAuth()
    {
        return !IsAuthInProgress;
    }

    private bool CanCancelAuth()
    {
        return IsAuthInProgress && _authCts is { IsCancellationRequested: false };
    }

    [RelayCommand(CanExecute = nameof(CanCancelAuth))]
    private async Task CancelAuthAsync()
    {
        if (_authCts is not { } pending)
        {
            return;
        }

        try
        {
            await pending.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelPendingAuthOnLeave()
    {
        if (_authCts is not { } pending)
        {
            return;
        }

        _authCts = null;
        _currentAuthUrl = null;
        IsAuthInProgress = false;
        UnsubscribeStatus();

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task StartAuthorizationAsync(Action<string> onAuthUrlReady)
    {
        if (_context is null || _authCts is not null)
        {
            return;
        }

        var settings = _context.Settings.Twitch;

        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            ResultText = "Не заполнены Client ID или Secret";
            ResultSeverity = StatusSeverity.Error;
            return;
        }

        var cts = new CancellationTokenSource();
        _authCts = cts;
        _currentAuthUrl = null;
        IsAuthInProgress = true;
        _logger.OAuthBrowserFlowStarted(_role, OAuthAuthorizationSurface.OnboardingWizard);
        StatusMessage = "Подготовка ссылки...";
        StatusMessageSeverity = StatusSeverity.Info;
        ResultText = string.Empty;
        SubscribeStatus();

        var hadError = false;

        try
        {
            var scopes = GetDefaultScopes();
            var redirectUri = string.IsNullOrWhiteSpace(settings.RedirectUri) ? null : settings.RedirectUri;

            var result = await _oauthService.StartOAuthFlowToDraftAsync(
                _role,
                settings.ClientId,
                settings.ClientSecret,
                scopes,
                redirectUri,
                onAuthUrlReady,
                checkBroadcasterChannel: false,
                cts.Token);

            if (!ReferenceEquals(_authCts, cts))
            {
                return;
            }

            hadError = !ApplyAuthResult(result);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            hadError = true;
            if (ReferenceEquals(_authCts, cts))
            {
                StatusMessage = "Авторизация отменена";
                StatusMessageSeverity = StatusSeverity.Warning;
                ResultText = string.Empty;
            }
        }
        catch (Exception exception) when (ReferenceEquals(_authCts, cts))
        {
            hadError = true;
            ResultText = SafeAuthMessage(exception);
            ResultSeverity = StatusSeverity.Error;
            StatusMessage = AuthErrorDetails(exception);
            StatusMessageSeverity = StatusSeverity.Error;
            _logger.OAuthFlowFailed(exception, _role);
        }
        finally
        {
            UnsubscribeStatus();

            if (ReferenceEquals(_authCts, cts))
            {
                _authCts = null;
                _currentAuthUrl = null;
                IsAuthInProgress = false;

                if (!hadError)
                {
                    RefreshFromContext();
                }
            }

            cts.Dispose();
        }
    }

    private bool ApplyAuthResult(OAuthFlowResult result)
    {
        if (_context is null)
        {
            return false;
        }

        var account = GetAccount();
        if (!_context.AcceptTwitchLogin(_role, account, result))
        {
            var expected = _context.GetExpectedTwitchAccountLabel(_role, _accountsStore, account);
            var actual = string.IsNullOrWhiteSpace(result.Login)
                ? "другой аккаунт"
                : $"@{result.Login.TrimStart('@')}";
            StatusMessage = expected is not null
                ? $"Вход выполнен в {actual}, но этому каналу назначен {expected}. Войдите в {expected} или измените назначение в разделе «Настройки -> Площадки и каналы»."
                : "Войдите с аккаунтом Twitch, назначенным этому каналу. Изменить назначение можно в разделе «Настройки -> Площадки и каналы».";
            StatusMessageSeverity = StatusSeverity.Error;
            return false;
        }

        if (_role != PlatformAuthRole.Broadcaster || _context.TwitchChannel is not null
            || !_context.AutoDetectChannel
            || string.IsNullOrWhiteSpace(result.Login))
        {
            return true;
        }

        _context.Settings.Twitch.Channel = result.Login;
        return true;
    }

    private void RefreshFromContext()
    {
        var account = _context?.CurrentTwitchAccount(_accountsStore, _role) ?? GetAccount();
        var expectedLabel = _context?.GetExpectedTwitchAccountLabel(_role, _accountsStore, account);
        ExpectedAccountLabel = expectedLabel;
        IsAccountFixed = expectedLabel is not null;
        ExpectedAccountHint = expectedLabel is not null
            ? $"Для этого канала назначен аккаунт {expectedLabel}. Изменить назначение можно в разделе «Настройки -> Площадки и каналы»."
            : string.Empty;

        HasToken = TwitchScopes.HasUsableAuthorization(_role, account) && _context?.CanUseTwitchRole(_role, account) == true;

        if (HasToken)
        {
            var login = string.IsNullOrWhiteSpace(account.Login) ? "–" : account.Login;
            ResultText = $"Авторизован: @{login}";
            ResultSeverity = StatusSeverity.Success;
            BroadcasterCapabilitiesHint = string.Empty;

            if (_authCts is null)
            {
                StatusMessage = IsAccountFixed
                    ? $"Можно идти дальше или обновить вход для {ExpectedAccountLabel}."
                    : "Можно идти дальше или сменить аккаунт.";
                StatusMessageSeverity = StatusSeverity.None;
            }

            CanAdvance = true;
        }
        else
        {
            var canSkip = _role == PlatformAuthRole.Broadcaster && _context?.HasConfiguredTwitchChannel == true;
            BroadcasterCapabilitiesHint = _role == PlatformAuthRole.Broadcaster
                ? (canSkip
                    ? "Без входа стримера можно нажать «Продолжить только с ботом» – тогда останутся недоступны опросы и управление трансляцией."
                    : "Вход стримера нужен для автоопределения канала, опросов и управления трансляцией.")
                : string.Empty;

            if (_authCts is null)
            {
                ResultText = PlatformAuthRoles.HasCredentials(account.AccessToken, account.RefreshToken)
                    ? "Нужно выдать разрешения заново"
                    : "Не авторизован";
                ResultSeverity = canSkip ? StatusSeverity.None : StatusSeverity.Error;
                StatusMessage = canSkip
                    ? "Можно продолжить только с ботом или войти аккаунтом стримера."
                    : "Откройте ссылку в браузере или войдите во встроенном окне.";
                StatusMessageSeverity = StatusSeverity.None;
            }

            CanAdvance = canSkip;
        }

        OnPropertyChanged(nameof(NextButtonTextOverride));
        OnPropertyChanged(nameof(IsNextActionSecondary));
    }

    private void OnCredentialsChanged(object? sender, EventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        var account = GetAccount();

        if (!PlatformAuthRoles.HasCredentials(account.AccessToken, account.RefreshToken))
        {
            return;
        }

        account.AccessToken = string.Empty;
        account.RefreshToken = string.Empty;
        account.AccessTokenExpiresAt = null;
        account.Scopes = [];
        account.StoredScopes = [];
        _context.ClearTwitchLogin(_role);

        RefreshFromContext();
    }

    private void OnOAuthStatusChanged(PlatformAuthRole role, string message)
    {
        if (role != _role)
        {
            return;
        }

        _uiDispatcher.Invoke(() => ApplyStatusMessage(message));
    }

    private void ApplyStatusMessage(string message)
    {
        StatusMessage = message;
        StatusMessageSeverity = StatusSeverity.Info;
    }

    private void SubscribeStatus()
    {
        if (_statusSubscribed)
        {
            return;
        }

        _oauthService.StatusChanged += OnOAuthStatusChanged;
        _statusSubscribed = true;
    }

    private void UnsubscribeStatus()
    {
        if (!_statusSubscribed)
        {
            return;
        }

        _oauthService.StatusChanged -= OnOAuthStatusChanged;
        _statusSubscribed = false;
    }

    private TwitchAccountSettings GetAccount()
    {
        if (_context is null)
        {
            throw new InvalidOperationException("OnEnter must be called before accessing context");
        }

        return _role == PlatformAuthRole.Broadcaster
            ? _context.BroadcasterAccount
            : _context.BotAccount;
    }

    private string[] GetDefaultScopes()
    {
        return _role == PlatformAuthRole.Broadcaster ? BroadcasterDefaultScopes : BotDefaultScopes;
    }

    private void OpenInBrowser(string authUrl)
    {
        if (_shellLauncher.Open(authUrl))
        {
            StatusMessage = "Браузер открыт. Подтвердите доступ.";
            StatusMessageSeverity = StatusSeverity.Info;
            return;
        }

        StatusMessage = "Не удалось открыть браузер. Скопируйте ссылку и откройте вручную.";
        StatusMessageSeverity = StatusSeverity.Error;
    }

    private void CopyToClipboard(string authUrl)
    {
        if (_clipboard.TrySetText(authUrl))
        {
            StatusMessage = "Ссылка скопирована. Откройте её в браузере и подтвердите доступ.";
            StatusMessageSeverity = StatusSeverity.Info;
            return;
        }

        StatusMessage = "Не удалось скопировать в буфер обмена.";
        StatusMessageSeverity = StatusSeverity.Error;
    }

    private static string SafeAuthMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "Авторизация отменена",
            TimeoutException => "Время ожидания авторизации истекло",
            HttpRequestException => "Ошибка сети при запросе токена",
            ArgumentException => "Не заполнены Client ID или Client Secret",
            _ => "Ошибка авторизации",
        };
    }

    private static string AuthErrorDetails(Exception exception)
    {
        return exception switch
        {
            InvalidOperationException invalid => invalid.Message,
            HttpRequestException => "Проверьте подключение к интернету.",
            TimeoutException => "Попробуйте ещё раз.",
            ArgumentException => "Заполните Client ID и Client Secret на предыдущем шаге.",
            _ => "Проверьте Client ID, Client Secret и Redirect URI.",
        };
    }
}
