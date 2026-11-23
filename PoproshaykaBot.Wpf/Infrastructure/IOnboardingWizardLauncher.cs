namespace PoproshaykaBot.Wpf.Infrastructure;

public interface IOnboardingWizardLauncher
{
    event EventHandler? Closed;

    bool HasBeenShown { get; }

    bool IsOpen => false;

    void Show();
}
