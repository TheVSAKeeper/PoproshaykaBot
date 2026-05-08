using PoproshaykaBot.Core.Server;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class CredentialsPage : UserControl, IOnboardingWizardPage
{
    private OnboardingContext? _context;
    private bool _suspendValidation;

    public CredentialsPage()
    {
        InitializeComponent();
    }

    public event EventHandler? CanAdvanceChanged;

    public string PageTitle => "Учётные данные приложения";

    public bool CanAdvance { get; private set; }

    public void OnEnter(OnboardingContext context)
    {
        _context = context;
        _suspendValidation = true;
        try
        {
            _channelTextBox.Text = context.Settings.Twitch.Channel;
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

        ValidateInputs();
    }

    public Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        WriteToContext(context);
        return Task.FromResult(true);
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
    }

    private void WriteToContext(OnboardingContext context)
    {
        context.Settings.Twitch.Channel = _channelTextBox.Text.Trim();
        context.Settings.Twitch.ClientId = _clientIdTextBox.Text.Trim();
        context.Settings.Twitch.ClientSecret = _clientSecretTextBox.Text.Trim();
        context.Settings.Twitch.RedirectUri = _redirectUriTextBox.Text.Trim();
    }

    private void ValidateInputs()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(_channelTextBox.Text))
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

        var newCanAdvance = !hasErrors;
        if (newCanAdvance != CanAdvance)
        {
            CanAdvance = newCanAdvance;
            CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
