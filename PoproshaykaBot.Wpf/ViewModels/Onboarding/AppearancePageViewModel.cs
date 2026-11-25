using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class AppearancePageViewModel(ThemeSelectionViewModel theme, ShellPreferences shell) : OnboardingPageViewModelBase
{
    public override string PageTitle => "Оформление";

    public AppearanceSettingsSectionViewModel Appearance { get; } = new(theme, shell);

    public override void OnEnter(OnboardingContext context)
    {
        CanAdvance = true;
    }
}
