namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public interface IOnboardingPageViewModel
{
    event EventHandler? CanAdvanceChanged;

    event EventHandler? IsBusyChanged;

    string PageTitle { get; }

    bool CanAdvance { get; }

    bool IsBusy { get; }

    bool IsApplicable(OnboardingContext context);

    void OnEnter(OnboardingContext context);

    Task<bool> OnLeavingAsync(OnboardingContext context);
}
