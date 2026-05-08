using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class WelcomePage : OnboardingPageBase
{
    private const string TwitchDevConsoleUrl = "https://dev.twitch.tv/console/apps";

    public WelcomePage()
    {
        InitializeComponent();
    }

    public override string PageTitle => "Добро пожаловать";

    public override void OnEnter(OnboardingContext context)
    {
        SetCanAdvance(true);
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
