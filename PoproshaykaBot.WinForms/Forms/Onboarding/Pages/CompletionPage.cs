namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class CompletionPage : UserControl, IOnboardingWizardPage
{
    private bool _saveTriggered;

    public CompletionPage()
    {
        InitializeComponent();
    }

    public event EventHandler? CanAdvanceChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? SaveAccountsRequested;

    public string PageTitle => "Готово";

    public bool CanAdvance => true;

    public void OnEnter(OnboardingContext context)
    {
        var botLogin = string.IsNullOrWhiteSpace(context.BotAccount.Login) ? "—" : context.BotAccount.Login;
        var broadcasterLogin = string.IsNullOrWhiteSpace(context.BroadcasterAccount.Login)
            ? "—"
            : context.BroadcasterAccount.Login;

        _summaryLabel.Text =
            $"Канал: {context.Settings.Twitch.Channel}"
            + Environment.NewLine
            + $"Аккаунт бота: @{botLogin}"
            + Environment.NewLine
            + $"Аккаунт стримера: @{broadcasterLogin}";

        if (_saveTriggered)
        {
            return;
        }

        _saveTriggered = true;
        SaveAccountsRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(true);
    }
}
