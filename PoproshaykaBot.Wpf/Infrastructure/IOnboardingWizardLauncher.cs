namespace PoproshaykaBot.Wpf.Infrastructure;

using Core.Settings.Onboarding;

public interface IOnboardingWizardLauncher
{
    event EventHandler? Closed;

    bool HasBeenShown { get; }

    bool IsOpen => false;

    void Show(OnboardingStep step = OnboardingStep.None);
}
