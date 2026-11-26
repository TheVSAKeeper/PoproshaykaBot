using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class WelcomePageViewModel : OnboardingPageViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    private bool _includeLegacyImport;

    public override string PageTitle => "Добро пожаловать";

    public string IntroText => "Этот мастер поможет настроить PoproshaykaBot за несколько шагов.";

    public string BodyText
    {
        get
        {
            var text =
                "Бот поддерживает Twitch, YouTube или обе площадки сразу (мультистрим), локальный оверлей чата для OBS, команды и историю стримов."
                + Environment.NewLine + Environment.NewLine
                + "На следующих шагах вы сможете выбрать оформление, подключить нужные площадки, настроить локальный HTTP-сервер и проверить работу бота в чате.";

            if (IncludeLegacyImport)
            {
                text += Environment.NewLine + Environment.NewLine
                    + "Если вы уже пользовались предыдущей версией (WinForms), на следующем шаге можно сразу перенести настройки, аккаунты и статистику.";
            }

            return text;
        }
    }

    public string HintText => "Нажмите «Далее →», чтобы продолжить.";

    public override void OnEnter(OnboardingContext context)
    {
        IncludeLegacyImport = context.IncludeLegacyImport;
        CanAdvance = true;
    }
}
