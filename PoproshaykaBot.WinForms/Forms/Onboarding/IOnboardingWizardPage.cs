namespace PoproshaykaBot.WinForms.Forms.Onboarding;

public interface IOnboardingWizardPage
{
    event EventHandler? CanAdvanceChanged;
    string PageTitle { get; }

    bool CanAdvance { get; }

    void OnEnter(OnboardingContext context);

    Task<bool> OnLeavingAsync(OnboardingContext context);
}
