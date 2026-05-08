using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class AuthorizationPage : UserControl, IOnboardingWizardPage
{
    private static readonly string[] BotDefaultScopes = [..TwitchScopes.BotRequired];
    private static readonly string[] BroadcasterDefaultScopes = [..TwitchScopes.BroadcasterRequired];

    private OnboardingContext? _context;
    private TwitchOAuthRole _role = TwitchOAuthRole.Bot;
    private CancellationTokenSource? _authCts;
    private bool _statusSubscribed;

    public AuthorizationPage()
    {
        InitializeComponent();
        Disposed += OnPageDisposed;
    }

    public event EventHandler? CanAdvanceChanged;

    [Inject]
    public ITwitchOAuthService OAuthService { get; internal init; } = null!;

    public TwitchOAuthRole Role
    {
        get => _role;
        set
        {
            _role = value;
            UpdateIntro();
        }
    }

    public string PageTitle => _role == TwitchOAuthRole.Broadcaster
        ? "Авторизация стримера"
        : "Авторизация бота";

    public bool CanAdvance { get; private set; }

    public void OnEnter(OnboardingContext context)
    {
        _context = context;

        if (!_statusSubscribed)
        {
            OAuthService.StatusChanged += OnOAuthStatusChanged;
            _statusSubscribed = true;
        }

        UpdateIntro();
        RefreshFromContext();
    }

    public Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(CanAdvance);
    }

    private void OnPageDisposed(object? sender, EventArgs e)
    {
        if (_statusSubscribed)
        {
            OAuthService.StatusChanged -= OnOAuthStatusChanged;
            _statusSubscribed = false;
        }

        _authCts?.Cancel();
        _authCts?.Dispose();
        _authCts = null;
    }

    private async void OnAuthButtonClicked(object? sender, EventArgs e)
    {
        if (_authCts is { } pending)
        {
            try
            {
                await pending.CancelAsync();
            }
            catch
            {
            }

            return;
        }

        if (_context == null)
        {
            return;
        }

        var settings = _context.Settings.Twitch;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            _resultLabel.Text = "Не заполнены Client ID или Secret";
            _resultLabel.ForeColor = Color.DarkRed;
            return;
        }

        var cts = new CancellationTokenSource();
        _authCts = cts;
        _authButton.Text = "Отменить";
        _statusLabel.Text = "Открытие браузера...";
        _resultLabel.Text = "";

        try
        {
            var scopes = _role == TwitchOAuthRole.Broadcaster ? BroadcasterDefaultScopes : BotDefaultScopes;
            var redirectUri = string.IsNullOrWhiteSpace(settings.RedirectUri) ? null : settings.RedirectUri;

            var result = await OAuthService.StartOAuthFlowToDraftAsync(_role,
                settings.ClientId,
                settings.ClientSecret,
                scopes,
                redirectUri,
                cts.Token);

            if (!ReferenceEquals(_authCts, cts))
            {
                return;
            }

            var account = GetAccount();
            account.AccessToken = result.AccessToken;
            account.RefreshToken = result.RefreshToken;
            account.Login = result.Login;
            account.UserId = result.UserId;
            account.Scopes = result.Scopes;
            account.AccessTokenExpiresAt = result.ExpiresInSeconds > 0
                ? DateTimeOffset.UtcNow.AddSeconds(result.ExpiresInSeconds)
                : null;

            RefreshFromContext();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _statusLabel.Text = "Авторизация отменена";
            _resultLabel.Text = "";
        }
        catch (Exception exception) when (ReferenceEquals(_authCts, cts))
        {
            _resultLabel.Text = SafeAuthMessage(exception);
            _resultLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = "";
            _authButton.Text = "Повторить";
        }
        finally
        {
            if (ReferenceEquals(_authCts, cts))
            {
                _authCts = null;

                if (!CanAdvance)
                {
                    _authButton.Text = "Авторизовать";
                }
                else
                {
                    _authButton.Text = "Сменить аккаунт";
                }
            }

            cts.Dispose();
        }
    }

    private void OnOAuthStatusChanged(TwitchOAuthRole role, string message)
    {
        if (role != _role || IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => _statusLabel.Text = message);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private static string SafeAuthMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "Авторизация отменена",
            TimeoutException => "Время ожидания авторизации истекло",
            HttpRequestException => "Ошибка сети при запросе токена",
            ArgumentException => "Не заполнены Client ID или Client Secret",
            _ => "Ошибка авторизации. Проверьте Client ID, Client Secret и Redirect URI.",
        };
    }

    private void RefreshFromContext()
    {
        var account = GetAccount();
        var hasToken = !string.IsNullOrWhiteSpace(account.AccessToken);

        if (hasToken)
        {
            var login = string.IsNullOrWhiteSpace(account.Login) ? "—" : account.Login;
            _resultLabel.Text = $"Авторизован: @{login}";
            _resultLabel.ForeColor = Color.Green;
            _statusLabel.Text = "Можно идти дальше или сменить аккаунт.";
            _authButton.Text = "Сменить аккаунт";
            SetCanAdvance(true);
        }
        else
        {
            _resultLabel.Text = "Не авторизован";
            _resultLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = "";
            _authButton.Text = "Авторизовать";
            SetCanAdvance(false);
        }
    }

    private TwitchAccountSettings GetAccount()
    {
        if (_context == null)
        {
            throw new InvalidOperationException("OnEnter must be called before accessing context");
        }

        return _role == TwitchOAuthRole.Broadcaster
            ? _context.BroadcasterAccount
            : _context.BotAccount;
    }

    private void UpdateIntro()
    {
        _intro.Text = _role == TwitchOAuthRole.Broadcaster
            ? "Авторизуйте аккаунт стримера. Этот токен нужен для управления опросами и информацией трансляции."
            : "Авторизуйте аккаунт бота. От его имени бот будет писать в чат и слушать сообщения.";
    }

    private void SetCanAdvance(bool value)
    {
        if (CanAdvance == value)
        {
            return;
        }

        CanAdvance = value;
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
