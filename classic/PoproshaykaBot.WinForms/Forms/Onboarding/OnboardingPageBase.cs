namespace PoproshaykaBot.WinForms.Forms.Onboarding;

public class OnboardingPageBase : UserControl, IOnboardingWizardPage
{
    public event EventHandler? CanAdvanceChanged;

    public virtual string PageTitle => string.Empty;

    public bool CanAdvance { get; private set; }

    public virtual void OnEnter(OnboardingContext context)
    {
    }

    public virtual Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(CanAdvance);
    }

    protected void SetCanAdvance(bool value)
    {
        if (CanAdvance == value)
        {
            return;
        }

        CanAdvance = value;
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
