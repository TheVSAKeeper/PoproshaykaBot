using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class WelcomePage : UserControl, IOnboardingWizardPage
{
    private const string TwitchDevConsoleUrl = "https://dev.twitch.tv/console/apps";

    public WelcomePage()
    {
        InitializeComponent();
    }

    public event EventHandler? CanAdvanceChanged
    {
        add { }
        remove { }
    }

    public string PageTitle => "Добро пожаловать";

    public bool CanAdvance => true;

    public void OnEnter(OnboardingContext context)
    {
    }

    public Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(true);
    }

    private void OnConsoleLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(TwitchDevConsoleUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
