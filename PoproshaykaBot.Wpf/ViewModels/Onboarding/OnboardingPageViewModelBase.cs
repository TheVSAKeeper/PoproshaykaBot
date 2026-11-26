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

    public virtual string? NextButtonTextOverride => NextButtonText;

    public virtual bool IsNextActionSecondary => false;

    public virtual bool AllowBackNavigation => true;

    public virtual string? SecondaryActionText => null;

    public virtual bool CanExecuteSecondaryAction => false;

    public virtual bool IsBusy => false;

    public virtual bool IsApplicable(OnboardingContext context) => true;

    public virtual void OnEnter(OnboardingContext context)
    {
    }

    public virtual void OnNavigatingBack(OnboardingContext context)
    {
    }

    public virtual Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(CanAdvance);
    }

    public virtual Task OnSecondaryActionAsync(OnboardingContext context)
    {
        return Task.CompletedTask;
    }

    public virtual bool TryHandleEscape() => false;

    protected void OnIsBusyChanged()
    {
        IsBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCanAdvanceChanged(bool value)
    {
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
