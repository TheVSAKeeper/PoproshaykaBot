using CommunityToolkit.Mvvm.ComponentModel;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public abstract partial class OnboardingPageViewModelBase : ObservableObject, IOnboardingPageViewModel
{
    [ObservableProperty]
    private bool _canAdvance;

    public event EventHandler? CanAdvanceChanged;

    public event EventHandler? IsBusyChanged;

    public abstract string PageTitle { get; }

    public virtual string? NextButtonText => null;

    public virtual bool IsBusy => false;

    public virtual bool IsApplicable(OnboardingContext context) => true;

    public virtual void OnEnter(OnboardingContext context)
    {
    }

    public virtual Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(CanAdvance);
    }

    protected void OnIsBusyChanged()
    {
        IsBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCanAdvanceChanged(bool value)
    {
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
