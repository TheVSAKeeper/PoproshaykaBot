using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Diagnostics;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class WelcomePageViewModel : OnboardingPageViewModelBase
{
    private const string TwitchDevConsoleUrl = "https://dev.twitch.tv/console/apps";

    private readonly ILogger<WelcomePageViewModel> _logger;

    public WelcomePageViewModel(ILogger<WelcomePageViewModel> logger)
    {
        _logger = logger;
    }

    public override string PageTitle => "Добро пожаловать";

    public string IntroText => "Этот мастер поможет настроить бота за несколько шагов.";

    public string BodyText =>
        "Понадобится приложение в Twitch Developer Console: оно даёт пару Client ID + Client Secret и принимает Redirect URI."
        + Environment.NewLine + Environment.NewLine
        + "Если приложения ещё нет – откройте консоль, создайте новое приложение, укажите Redirect URI http://localhost:3000 (порт можно поменять)."
        + Environment.NewLine + Environment.NewLine
        + "Если уже есть – переходите к следующему шагу.";

    public string HintText => "Нажмите «Далее», чтобы продолжить.";

    public override void OnEnter(OnboardingContext context)
    {
        CanAdvance = true;
    }

    [RelayCommand]
    private void OpenDevConsole()
    {
        try
        {
            Process.Start(new ProcessStartInfo(TwitchDevConsoleUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logger.OnboardingDevConsoleOpenFailed(exception);
        }
    }
}
