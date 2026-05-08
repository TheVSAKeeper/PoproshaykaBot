using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Onboarding;
using PoproshaykaBot.WinForms.Infrastructure.Di;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class CredentialsPage : OnboardingPageBase
{
    private OnboardingContext? _context;
    private bool _suspendValidation;
    private string _initialChannel = string.Empty;
    private string _initialClientId = string.Empty;
    private string _initialClientSecret = string.Empty;
    private string _initialRedirectUri = string.Empty;
    private CancellationTokenSource? _clientValidationCts;
    private CancellationTokenSource? _channelValidationCts;
    private string _lastValidatedClientId = string.Empty;
    private string _lastValidatedClientSecret = string.Empty;
    private string _lastValidatedChannel = string.Empty;
    private string _lastValidatedChannelClientId = string.Empty;

    public CredentialsPage()
    {
        InitializeComponent();
        Disposed += OnPageDisposed;
    }

    [Inject]
    public IClientCredentialsValidator ClientCredentialsValidator { get; internal init; } = null!;

    [Inject]
    public IOnboardingChannelValidator ChannelValidator { get; internal init; } = null!;

    public override string PageTitle => "Учётные данные приложения";

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        _suspendValidation = true;

        try
        {
            _autoDetectChannelCheckBox.Checked = context.AutoDetectChannel;

            var contextChannel = context.Settings.Twitch.Channel;
            var broadcasterLogin = context.BroadcasterAccount.Login;

            if (context.AutoDetectChannel && !string.IsNullOrWhiteSpace(broadcasterLogin))
            {
                _channelTextBox.Text = broadcasterLogin;
            }
            else
            {
                _channelTextBox.Text = contextChannel;
            }

            ApplyAutoDetectVisuals();

            _clientIdTextBox.Text = context.Settings.Twitch.ClientId;
            _clientSecretTextBox.Text = context.Settings.Twitch.ClientSecret;
            _redirectUriTextBox.Text = string.IsNullOrWhiteSpace(context.Settings.Twitch.RedirectUri)
                ? "http://localhost:3000"
                : context.Settings.Twitch.RedirectUri;
        }
        finally
        {
            _suspendValidation = false;
        }

        SnapshotInitialValues();
        _lastValidatedClientId = string.Empty;
        _lastValidatedClientSecret = string.Empty;
        _lastValidatedChannel = string.Empty;
        _lastValidatedChannelClientId = string.Empty;
        ValidateInputs();
        ScheduleClientCredentialsCheck();
        ScheduleChannelCheck();
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        WriteToContext(context);

        if (!HasCredentialsChanged())
        {
            return Task.FromResult(CanAdvance);
        }

        SnapshotInitialValues();
        context.RaiseCredentialsChanged();

        return Task.FromResult(CanAdvance);
    }

    private void OnPageDisposed(object? sender, EventArgs e)
    {
        _clientValidationCts?.Cancel();
        _clientValidationCts?.Dispose();
        _clientValidationCts = null;

        _channelValidationCts?.Cancel();
        _channelValidationCts?.Dispose();
        _channelValidationCts = null;
    }

    private void OnInputChanged(object? sender, EventArgs e)
    {
        if (_suspendValidation)
        {
            return;
        }

        ValidateInputs();

        if (_context != null)
        {
            WriteToContext(_context);
        }

        if (ReferenceEquals(sender, _clientIdTextBox) || ReferenceEquals(sender, _clientSecretTextBox))
        {
            ScheduleClientCredentialsCheck();
            ScheduleChannelCheck();
        }
        else if (ReferenceEquals(sender, _channelTextBox))
        {
            ScheduleChannelCheck();
        }
    }

    private void OnAutoDetectChannelCheckedChanged(object? sender, EventArgs e)
    {
        if (_context != null)
        {
            _context.AutoDetectChannel = _autoDetectChannelCheckBox.Checked;
        }

        if (_suspendValidation)
        {
            return;
        }

        _suspendValidation = true;
        try
        {
            if (_autoDetectChannelCheckBox.Checked && _context is not null)
            {
                var login = _context.BroadcasterAccount.Login;
                if (!string.IsNullOrWhiteSpace(login))
                {
                    _channelTextBox.Text = login;
                }
            }

            ApplyAutoDetectVisuals();
        }
        finally
        {
            _suspendValidation = false;
        }

        ValidateInputs();

        if (_context != null)
        {
            WriteToContext(_context);
        }

        ScheduleChannelCheck();
    }

    private async void OnValidationTimerTick(object? sender, EventArgs e)
    {
        _validationTimer.Stop();
        await RunCredentialsCheckAsync();
    }

    private async void OnChannelValidationTimerTick(object? sender, EventArgs e)
    {
        _channelValidationTimer.Stop();
        await RunChannelCheckAsync();
    }

    private void ApplyAutoDetectVisuals()
    {
        var auto = _autoDetectChannelCheckBox.Checked;
        _channelTextBox.ReadOnly = auto;

        if (auto)
        {
            var login = _context?.BroadcasterAccount.Login ?? string.Empty;

            if (string.IsNullOrWhiteSpace(login))
            {
                _channelStatusLabel.Text = "Канал заполнится автоматически после авторизации стримера.";
                _channelStatusLabel.ForeColor = Color.Gray;
            }
            else
            {
                _channelStatusLabel.Text = $"Использован логин стримера: @{login}";
                _channelStatusLabel.ForeColor = Color.Gray;
            }
        }
        else
        {
            _channelStatusLabel.Text = string.Empty;
        }
    }

    private void ScheduleClientCredentialsCheck()
    {
        _clientValidationCts?.Cancel();

        var clientId = _clientIdTextBox.Text.Trim();
        var clientSecret = _clientSecretTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _validationTimer.Stop();
            _clientStatusLabel.Text = string.Empty;
            return;
        }

        _validationTimer.Stop();
        _validationTimer.Start();

        _clientStatusLabel.Text = "Ожидание ввода...";
        _clientStatusLabel.ForeColor = Color.Gray;
    }

    private async Task RunCredentialsCheckAsync()
    {
        var clientId = _clientIdTextBox.Text.Trim();
        var clientSecret = _clientSecretTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _clientStatusLabel.Text = string.Empty;
            return;
        }

        if (string.Equals(clientId, _lastValidatedClientId, StringComparison.Ordinal)
            && string.Equals(clientSecret, _lastValidatedClientSecret, StringComparison.Ordinal))
        {
            return;
        }

        var previousCts = _clientValidationCts;
        _clientValidationCts = new();
        var token = _clientValidationCts.Token;
        previousCts?.Cancel();
        previousCts?.Dispose();

        _clientStatusLabel.Text = "Проверка Client ID и Secret...";
        _clientStatusLabel.ForeColor = Color.Blue;

        ClientCredentialsValidationResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));
            result = await ClientCredentialsValidator.ValidateAsync(clientId, clientSecret, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            _clientStatusLabel.Text = "Ошибка проверки";
            _clientStatusLabel.ForeColor = Color.Gray;
            return;
        }

        if (token.IsCancellationRequested || IsDisposed)
        {
            return;
        }

        if (!string.Equals(clientId, _clientIdTextBox.Text.Trim(), StringComparison.Ordinal)
            || !string.Equals(clientSecret, _clientSecretTextBox.Text.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _lastValidatedClientId = clientId;
        _lastValidatedClientSecret = clientSecret;

        switch (result)
        {
            case ClientCredentialsValidationResult.Valid:
                _clientStatusLabel.Text = "✓ Client ID и Secret валидны";
                _clientStatusLabel.ForeColor = Color.Green;
                break;

            case ClientCredentialsValidationResult.Invalid:
                _clientStatusLabel.Text = "✗ Client ID или Secret недействительны";
                _clientStatusLabel.ForeColor = Color.DarkRed;
                break;

            case ClientCredentialsValidationResult.NetworkError:
                _clientStatusLabel.Text = "Не удалось проверить (сеть)";
                _clientStatusLabel.ForeColor = Color.Gray;
                break;

            default:
                _clientStatusLabel.Text = string.Empty;
                break;
        }
    }

    private void ScheduleChannelCheck()
    {
        _channelValidationCts?.Cancel();
        _channelValidationTimer.Stop();

        var channel = _channelTextBox.Text.Trim();
        var clientId = _clientIdTextBox.Text.Trim();
        var clientSecret = _clientSecretTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        if (_autoDetectChannelCheckBox.Checked)
        {
            return;
        }

        _channelValidationTimer.Start();
    }

    private async Task RunChannelCheckAsync()
    {
        var channel = _channelTextBox.Text.Trim();
        var clientId = _clientIdTextBox.Text.Trim();
        var clientSecret = _clientSecretTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(channel)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return;
        }

        if (_autoDetectChannelCheckBox.Checked)
        {
            return;
        }

        if (string.Equals(channel, _lastValidatedChannel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(clientId, _lastValidatedChannelClientId, StringComparison.Ordinal))
        {
            return;
        }

        var previousCts = _channelValidationCts;
        _channelValidationCts = new();
        var token = _channelValidationCts.Token;
        await previousCts?.CancelAsync();
        previousCts?.Dispose();

        var statusLabel = _channelStatusLabel;
        statusLabel.Text = "Проверка канала...";
        statusLabel.ForeColor = Color.Blue;

        ChannelValidationResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
            result = await ChannelValidator.ValidateWithAppTokenAsync(channel, clientId, clientSecret, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            statusLabel.Text = "Не удалось проверить канал";
            statusLabel.ForeColor = Color.Gray;
            return;
        }

        if (token.IsCancellationRequested || IsDisposed)
        {
            return;
        }

        if (!string.Equals(channel, _channelTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(clientId, _clientIdTextBox.Text.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _lastValidatedChannel = channel;
        _lastValidatedChannelClientId = clientId;

        switch (result)
        {
            case ChannelValidationResult.Found:
                statusLabel.Text = $"✓ Канал @{channel} найден на Twitch";
                statusLabel.ForeColor = Color.Green;
                break;

            case ChannelValidationResult.NotFound:
                statusLabel.Text = $"✗ Канал @{channel} не найден на Twitch";
                statusLabel.ForeColor = Color.DarkRed;
                break;

            case ChannelValidationResult.Skipped:
            default:
                statusLabel.Text = "Не удалось проверить канал";
                statusLabel.ForeColor = Color.Gray;
                break;
        }
    }

    private void SnapshotInitialValues()
    {
        _initialChannel = _channelTextBox.Text.Trim();
        _initialClientId = _clientIdTextBox.Text.Trim();
        _initialClientSecret = _clientSecretTextBox.Text.Trim();
        _initialRedirectUri = _redirectUriTextBox.Text.Trim();
    }

    private bool HasCredentialsChanged()
    {
        return !string.Equals(_initialChannel, _channelTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
               || !string.Equals(_initialClientId, _clientIdTextBox.Text.Trim(), StringComparison.Ordinal)
               || !string.Equals(_initialClientSecret, _clientSecretTextBox.Text.Trim(), StringComparison.Ordinal)
               || !string.Equals(_initialRedirectUri, _redirectUriTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void WriteToContext(OnboardingContext context)
    {
        context.AutoDetectChannel = _autoDetectChannelCheckBox.Checked;
        context.Settings.Twitch.Channel = _channelTextBox.Text.Trim();
        context.Settings.Twitch.ClientId = _clientIdTextBox.Text.Trim();
        context.Settings.Twitch.ClientSecret = _clientSecretTextBox.Text.Trim();
        context.Settings.Twitch.RedirectUri = _redirectUriTextBox.Text.Trim();
    }

    private void ValidateInputs()
    {
        var errors = new List<string>();

        var channelEmpty = string.IsNullOrWhiteSpace(_channelTextBox.Text);
        if (channelEmpty && !_autoDetectChannelCheckBox.Checked)
        {
            errors.Add("канал");
        }

        if (string.IsNullOrWhiteSpace(_clientIdTextBox.Text))
        {
            errors.Add("Client ID");
        }

        if (string.IsNullOrWhiteSpace(_clientSecretTextBox.Text))
        {
            errors.Add("Client Secret");
        }

        var redirectUri = _redirectUriTextBox.Text.Trim();
        var redirectValid = RedirectUriPortResolver.TryResolve(redirectUri, out var port);

        if (!redirectValid)
        {
            errors.Add("Redirect URI (некорректный формат)");
            _portHintLabel.Text = "";
        }
        else
        {
            _portHintLabel.Text = $"HTTP сервер будет слушать порт {port}";
        }

        var hasErrors = errors.Count > 0;

        _validationLabel.Text = hasErrors
            ? $"Заполните: {string.Join(", ", errors)}"
            : "";

        SetCanAdvance(!hasErrors);
    }
}
