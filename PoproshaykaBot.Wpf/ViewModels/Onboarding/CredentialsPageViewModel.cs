using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class CredentialsPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private const string TwitchDevConsoleUrl = "https://dev.twitch.tv/console/apps";
    internal const string DefaultRedirectUri = "http://localhost:8080";

    private readonly IClientCredentialsValidator _clientCredentialsValidator;
    private readonly IOnboardingChannelValidator _channelValidator;
    private readonly ILogger<CredentialsPageViewModel> _logger;
    private readonly IShellLauncher? _shellLauncher;
    private readonly IClipboardService? _clipboard;

    private readonly IUiTimer _clientDebounce;
    private readonly IUiTimer _channelDebounce;

    private OnboardingContext? _context;
    private CancellationTokenSource? _clientValidationCts;
    private CancellationTokenSource? _channelValidationCts;

    private bool _suspendValidation;
    private string _initialChannel = string.Empty;
    private string _initialClientId = string.Empty;
    private string _initialClientSecret = string.Empty;
    private string _initialRedirectUri = string.Empty;
    private string _lastValidatedClientId = string.Empty;
    private string _lastValidatedClientSecret = string.Empty;
    private ClientCredentialsValidationResult? _lastClientValidationResult;
    private string _lastValidatedChannel = string.Empty;
    private string _lastValidatedChannelClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChannelReadOnly))]
    [NotifyPropertyChangedFor(nameof(ChannelFieldLabel))]
    private bool _autoDetectChannel = true;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveRedirectUri))]
    [NotifyPropertyChangedFor(nameof(InstructionText))]
    private string _redirectUri = string.Empty;

    [ObservableProperty]
    private string _channelStatusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _channelStatusSeverity = StatusSeverity.None;

    [ObservableProperty]
    private bool _canRetryChannelCheck;

    [ObservableProperty]
    private string _clientStatusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _clientStatusSeverity = StatusSeverity.None;

    [ObservableProperty]
    private bool _canRetryClientCheck;

    [ObservableProperty]
    private string _portHintText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string _validationErrorText = string.Empty;

    [ObservableProperty]
    private string _redirectUriCopyFeedback = string.Empty;

    private bool _disposed;

    public CredentialsPageViewModel(
        IClientCredentialsValidator clientCredentialsValidator,
        IOnboardingChannelValidator channelValidator,
        ILogger<CredentialsPageViewModel> logger,
        IUiDispatcher uiDispatcher,
        IShellLauncher? shellLauncher = null,
        IClipboardService? clipboard = null)
    {
        _clientCredentialsValidator = clientCredentialsValidator;
        _channelValidator = channelValidator;
        _logger = logger;
        _shellLauncher = shellLauncher;
        _clipboard = clipboard;

        _clientDebounce = uiDispatcher.CreateAsyncTimer(
            TimeSpan.FromMilliseconds(800),
            OnClientDebounceTickAsync,
            exception => _logger.OnboardingCredentialsCheckFailed(exception));

        _channelDebounce = uiDispatcher.CreateAsyncTimer(
            TimeSpan.FromMilliseconds(800),
            OnChannelDebounceTickAsync,
            exception => _logger.OnboardingChannelCredentialsCheckFailed(exception, Channel.Trim()));
    }

    internal TimeSpan ClientValidationTimeout { get; set; } = TimeSpan.FromSeconds(6);

    internal TimeSpan ChannelValidationTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public override string PageTitle => "Настройка Twitch";

    public bool IsChannelReadOnly => AutoDetectChannel;

    public string ChannelFieldLabel => AutoDetectChannel
        ? "Канал (определится автоматически)"
        : "Канал (обязательно)";

    public string EffectiveRedirectUri => string.IsNullOrWhiteSpace(RedirectUri)
        ? DefaultRedirectUri
        : RedirectUri.Trim();

    public string InstructionText =>
        $"Укажите Client ID и Client Secret из Twitch Developer Console и добавьте там OAuth Redirect URL: {EffectiveRedirectUri}";

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationErrorText);

    public override bool IsApplicable(OnboardingContext context) => context.Settings.Twitch.Enabled;

    [RelayCommand]
    private void OpenDevConsole()
    {
        _shellLauncher?.Open(TwitchDevConsoleUrl);
    }

    [RelayCommand]
    private void CopyRedirectUri()
    {
        var uri = EffectiveRedirectUri;
        if (_clipboard?.TrySetText(uri) == true)
        {
            RedirectUriCopyFeedback = $"Скопировано: {uri}";
            return;
        }

        RedirectUriCopyFeedback = "Не удалось скопировать Redirect URI.";
    }

    [RelayCommand]
    private Task RetryClientValidationAsync()
    {
        _lastValidatedClientId = string.Empty;
        _lastValidatedClientSecret = string.Empty;
        _lastClientValidationResult = null;
        CanRetryClientCheck = false;
        ValidateInputs();
        return RunCredentialsCheckAsync();
    }

    [RelayCommand]
    private Task RetryChannelValidationAsync()
    {
        _lastValidatedChannel = string.Empty;
        _lastValidatedChannelClientId = string.Empty;
        CanRetryChannelCheck = false;
        return RunChannelCheckAsync();
    }

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        _suspendValidation = true;
        try
        {
            AutoDetectChannel = context.AutoDetectChannel;

            var contextChannel = context.Settings.Twitch.Channel;
            var broadcasterLogin = context.BroadcasterAccount.Login;

            if (context.AutoDetectChannel && !string.IsNullOrWhiteSpace(broadcasterLogin))
            {
                Channel = broadcasterLogin;
            }
            else
            {
                Channel = contextChannel;
            }

            ApplyAutoDetectChannelStatus(context);

            ClientId = context.Settings.Twitch.ClientId;
            ClientSecret = context.Settings.Twitch.ClientSecret;
            RedirectUri = string.IsNullOrWhiteSpace(context.Settings.Twitch.RedirectUri)
                ? DefaultRedirectUri
                : context.Settings.Twitch.RedirectUri;
            RedirectUriCopyFeedback = string.Empty;
        }
        finally
        {
            _suspendValidation = false;
        }

        SnapshotInitialValues();
        _lastValidatedClientId = string.Empty;
        _lastValidatedClientSecret = string.Empty;
        _lastClientValidationResult = null;
        _lastValidatedChannel = string.Empty;
        _lastValidatedChannelClientId = string.Empty;
        CanRetryClientCheck = false;
        CanRetryChannelCheck = false;

        ValidateInputs();
        ScheduleClientCredentialsCheck();
        ScheduleChannelCheck();
    }

    public override void OnNavigatingBack(OnboardingContext context)
    {
        WriteToContext(context);

        if (HasCredentialsChanged())
        {
            SnapshotInitialValues();
            context.RaiseCredentialsChanged();
        }
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        WriteToContext(context);

        if (HasCredentialsChanged())
        {
            SnapshotInitialValues();
            context.RaiseCredentialsChanged();
        }

        return Task.FromResult(CanAdvance);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _clientDebounce.Stop();
        _channelDebounce.Stop();

        _clientValidationCts?.Cancel();
        _clientValidationCts?.Dispose();
        _clientValidationCts = null;

        _channelValidationCts?.Cancel();
        _channelValidationCts?.Dispose();
        _channelValidationCts = null;
    }

    partial void OnChannelChanged(string value)
    {
        if (_suspendValidation)
        {
            return;
        }

        ValidateInputs();
        ScheduleChannelCheck();
    }

    partial void OnClientIdChanged(string value)
    {
        OnClientCredentialsChanged();
    }

    partial void OnClientSecretChanged(string value)
    {
        OnClientCredentialsChanged();
    }

    private void OnClientCredentialsChanged()
    {
        if (_suspendValidation)
        {
            return;
        }

        ValidateInputs();
        ScheduleClientCredentialsCheck();
        ScheduleChannelCheck();
    }

    partial void OnRedirectUriChanged(string value)
    {
        if (_suspendValidation)
        {
            return;
        }

        RedirectUriCopyFeedback = string.Empty;
        ValidateInputs();
    }

    partial void OnAutoDetectChannelChanged(bool value)
    {
        if (_suspendValidation)
        {
            return;
        }

        ApplyAutoDetectChannelStatus(_context);
        ValidateInputs();
        ScheduleChannelCheck();
    }

    private void ApplyAutoDetectChannelStatus(OnboardingContext? context)
    {
        CanRetryChannelCheck = false;
        if (!AutoDetectChannel)
        {
            ChannelStatusText = string.Empty;
            ChannelStatusSeverity = StatusSeverity.None;
            return;
        }

        var login = context?.BroadcasterAccount.Login;
        if (string.IsNullOrWhiteSpace(login))
        {
            ChannelStatusText = "Канал заполнится автоматически после авторизации стримера.";
            ChannelStatusSeverity = StatusSeverity.Info;
        }
        else
        {
            ChannelStatusText = $"Использован логин стримера: @{login}";
            ChannelStatusSeverity = StatusSeverity.Info;
        }
    }

    private void ScheduleClientCredentialsCheck()
    {
        _clientValidationCts?.Cancel();
        _clientDebounce.Stop();
        CanRetryClientCheck = false;

        var clientId = ClientId.Trim();
        var clientSecret = ClientSecret.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            ClientStatusText = string.Empty;
            ClientStatusSeverity = StatusSeverity.None;
            return;
        }

        if (string.Equals(clientId, _lastValidatedClientId, StringComparison.Ordinal)
            && string.Equals(clientSecret, _lastValidatedClientSecret, StringComparison.Ordinal)
            && _lastClientValidationResult is ClientCredentialsValidationResult.Valid or ClientCredentialsValidationResult.Invalid)
        {
            return;
        }

        ClientStatusText = "Ожидание ввода...";
        ClientStatusSeverity = StatusSeverity.None;
        _clientDebounce.Start();
    }

    private void ScheduleChannelCheck()
    {
        _channelValidationCts?.Cancel();
        _channelDebounce.Stop();

        if (AutoDetectChannel)
        {
            return;
        }

        var channel = Channel.Trim();
        var clientId = ClientId.Trim();
        var clientSecret = ClientSecret.Trim();

        if (string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            ChannelStatusText = string.Empty;
            ChannelStatusSeverity = StatusSeverity.None;
            CanRetryChannelCheck = false;
            _lastValidatedChannel = string.Empty;
            _lastValidatedChannelClientId = string.Empty;
            return;
        }

        if (!string.Equals(channel, _lastValidatedChannel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(clientId, _lastValidatedChannelClientId, StringComparison.Ordinal))
        {
            ChannelStatusText = string.Empty;
            ChannelStatusSeverity = StatusSeverity.None;
            CanRetryChannelCheck = false;
        }

        _channelDebounce.Start();
    }

    private Task OnClientDebounceTickAsync()
    {
        _clientDebounce.Stop();

        return RunCredentialsCheckAsync();
    }

    private Task OnChannelDebounceTickAsync()
    {
        _channelDebounce.Stop();

        return RunChannelCheckAsync();
    }

    private async Task RunCredentialsCheckAsync()
    {
        var clientId = ClientId.Trim();
        var clientSecret = ClientSecret.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            ClientStatusText = string.Empty;
            ClientStatusSeverity = StatusSeverity.None;
            CanRetryClientCheck = false;
            return;
        }

        if (string.Equals(clientId, _lastValidatedClientId, StringComparison.Ordinal)
            && string.Equals(clientSecret, _lastValidatedClientSecret, StringComparison.Ordinal))
        {
            return;
        }

        var previousCts = _clientValidationCts;
        _clientValidationCts = new CancellationTokenSource();
        var token = _clientValidationCts.Token;

        if (previousCts is not null)
        {
            await previousCts.CancelAsync();
            previousCts.Dispose();
        }

        ClientStatusText = "Проверка Client ID и Secret...";
        ClientStatusSeverity = StatusSeverity.Info;
        CanRetryClientCheck = false;

        ClientCredentialsValidationResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(ClientValidationTimeout);
            result = await _clientCredentialsValidator.ValidateAsync(clientId, clientSecret, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_disposed)
        {
            if (string.Equals(clientId, ClientId.Trim(), StringComparison.Ordinal)
                && string.Equals(clientSecret, ClientSecret.Trim(), StringComparison.Ordinal))
            {
                ClientStatusText = "Превышено время ожидания проверки";
                ClientStatusSeverity = StatusSeverity.Warning;
                CanRetryClientCheck = true;
            }

            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.OnboardingCredentialsCheckFailed(exception);
            ClientStatusText = "Ошибка проверки";
            ClientStatusSeverity = StatusSeverity.Warning;
            CanRetryClientCheck = true;
            return;
        }

        if (token.IsCancellationRequested || _disposed)
        {
            return;
        }

        if (!string.Equals(clientId, ClientId.Trim(), StringComparison.Ordinal)
            || !string.Equals(clientSecret, ClientSecret.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _lastValidatedClientId = clientId;
        _lastValidatedClientSecret = clientSecret;
        _lastClientValidationResult = result;

        switch (result)
        {
            case ClientCredentialsValidationResult.Valid:
                ClientStatusText = "Client ID и Secret действительны";
                ClientStatusSeverity = StatusSeverity.Success;
                CanRetryClientCheck = false;
                break;

            case ClientCredentialsValidationResult.Invalid:
                ClientStatusText = "Client ID или Secret недействительны";
                ClientStatusSeverity = StatusSeverity.Error;
                CanRetryClientCheck = false;
                break;

            case ClientCredentialsValidationResult.NetworkError:
                ClientStatusText = "Не удалось проверить (сеть)";
                ClientStatusSeverity = StatusSeverity.Warning;
                CanRetryClientCheck = true;
                break;

            default:
                ClientStatusText = string.Empty;
                ClientStatusSeverity = StatusSeverity.None;
                CanRetryClientCheck = false;
                break;
        }

        ValidateInputs();
    }

    private async Task RunChannelCheckAsync()
    {
        var channel = Channel.Trim();
        var clientId = ClientId.Trim();
        var clientSecret = ClientSecret.Trim();

        if (string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || AutoDetectChannel)
        {
            return;
        }

        if (string.Equals(channel, _lastValidatedChannel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(clientId, _lastValidatedChannelClientId, StringComparison.Ordinal))
        {
            return;
        }

        var previousCts = _channelValidationCts;
        _channelValidationCts = new CancellationTokenSource();
        var token = _channelValidationCts.Token;

        if (previousCts is not null)
        {
            await previousCts.CancelAsync();
            previousCts.Dispose();
        }

        ChannelStatusText = "Проверка канала...";
        ChannelStatusSeverity = StatusSeverity.Info;
        CanRetryChannelCheck = false;

        ChannelValidationResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(ChannelValidationTimeout);
            result = await _channelValidator.ValidateWithAppTokenAsync(channel, clientId, clientSecret, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_disposed)
        {
            if (!AutoDetectChannel
                && string.Equals(channel, Channel.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(clientId, ClientId.Trim(), StringComparison.Ordinal))
            {
                ChannelStatusText = "Превышено время ожидания проверки";
                ChannelStatusSeverity = StatusSeverity.Warning;
                CanRetryChannelCheck = true;
            }

            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.OnboardingChannelCredentialsCheckFailed(exception, channel);
            ChannelStatusText = "Не удалось проверить канал";
            ChannelStatusSeverity = StatusSeverity.Warning;
            CanRetryChannelCheck = true;
            return;
        }

        if (token.IsCancellationRequested || _disposed)
        {
            return;
        }

        if (!string.Equals(channel, Channel.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(clientId, ClientId.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _lastValidatedChannel = channel;
        _lastValidatedChannelClientId = clientId;

        switch (result)
        {
            case ChannelValidationResult.Found:
                ChannelStatusText = $"Канал @{channel} найден на Twitch";
                ChannelStatusSeverity = StatusSeverity.Success;
                CanRetryChannelCheck = false;
                break;

            case ChannelValidationResult.NotFound:
                ChannelStatusText = $"Канал @{channel} не найден на Twitch";
                ChannelStatusSeverity = StatusSeverity.Error;
                CanRetryChannelCheck = false;
                break;

            default:
                ChannelStatusText = "Не удалось проверить канал";
                ChannelStatusSeverity = StatusSeverity.Warning;
                CanRetryChannelCheck = true;
                break;
        }
    }

    private bool IsClientCredentialsConfirmedInvalid =>
        _lastClientValidationResult == ClientCredentialsValidationResult.Invalid
        && string.Equals(ClientId.Trim(), _lastValidatedClientId, StringComparison.Ordinal)
        && string.Equals(ClientSecret.Trim(), _lastValidatedClientSecret, StringComparison.Ordinal);

    private void ValidateInputs()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Channel) && !AutoDetectChannel)
        {
            errors.Add("канал");
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errors.Add("Client ID");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            errors.Add("Client Secret");
        }

        var redirectValid = RedirectUriPortResolver.TryResolve(RedirectUri, out var port);
        if (!redirectValid)
        {
            errors.Add("Redirect URI (некорректный формат)");
            PortHintText = string.Empty;
        }
        else
        {
            PortHintText = $"HTTP сервер будет слушать порт {port}";
        }

        if (errors.Count > 0)
        {
            ValidationErrorText = $"Заполните: {string.Join(", ", errors)}";
            CanAdvance = false;
            return;
        }

        if (IsClientCredentialsConfirmedInvalid)
        {
            ValidationErrorText = "Client ID или Client Secret отклонены Twitch – проверьте значения перед продолжением.";
            CanAdvance = false;
            return;
        }

        ValidationErrorText = string.Empty;
        CanAdvance = true;
    }

    private void SnapshotInitialValues()
    {
        _initialChannel = Channel.Trim();
        _initialClientId = ClientId.Trim();
        _initialClientSecret = ClientSecret.Trim();
        _initialRedirectUri = RedirectUri.Trim();
    }

    private bool HasCredentialsChanged()
    {
        return !string.Equals(_initialChannel, Channel.Trim(), StringComparison.OrdinalIgnoreCase)
               || !string.Equals(_initialClientId, ClientId.Trim(), StringComparison.Ordinal)
               || !string.Equals(_initialClientSecret, ClientSecret.Trim(), StringComparison.Ordinal)
               || !string.Equals(_initialRedirectUri, RedirectUri.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void WriteToContext(OnboardingContext context)
    {
        context.AutoDetectChannel = AutoDetectChannel;
        context.Settings.Twitch.Channel = Channel.Trim();
        context.Settings.Twitch.ClientId = ClientId.Trim();
        context.Settings.Twitch.ClientSecret = ClientSecret.Trim();
        context.Settings.Twitch.RedirectUri = RedirectUri.Trim();
    }
}
