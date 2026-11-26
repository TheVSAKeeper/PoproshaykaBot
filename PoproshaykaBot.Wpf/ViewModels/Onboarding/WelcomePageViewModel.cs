using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class WelcomePageViewModel : OnboardingPageViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LegacyPathDescription))]
    [NotifyPropertyChangedFor(nameof(HintText))]
    private bool _includeLegacyImport;

    public override string PageTitle => "Добро пожаловать";

    public string IntroText => "Настройте PoproshaykaBot за несколько шагов.";

    public string SummaryText =>
        "Бот поддерживает Twitch, YouTube или обе площадки сразу (мультистрим), локальный оверлей чата для OBS, команды и историю стримов.";

    public string PathsHeading => "Два способа начать работу";

    public string LegacyPathTitle => "1. Перенести профиль из предыдущей версии";

    public string LegacyPathDescription => IncludeLegacyImport
        ? "Подготовьте папку со старой версией (WinForms) – на следующем шаге можно сразу перенести настройки, аккаунты и статистику."
        : "Подготовьте папку со старой версией (WinForms), если хотите перенести настройки, аккаунты и статистику позже в разделе «Настройки».";

    public string ScratchPathTitle => "2. Настроить с нуля";

    public string ScratchTwitchText =>
        "Для Twitch: аккаунт бота, а также Client ID и Client Secret из Twitch Developer Console (для управления стримом – ещё аккаунт стримера).";

    public string ScratchYouTubeText =>
        "Для YouTube: проект Google Cloud с OAuth-ключами и аккаунт бота.";

    public string HintText => IncludeLegacyImport
        ? "На следующем шаге откроется перенос данных из предыдущей версии."
        : "На следующем шаге вы выберете тему и размер текста.";

    public override void OnEnter(OnboardingContext context)
    {
        IncludeLegacyImport = context.IncludeLegacyImport;
        CanAdvance = true;
    }
}
