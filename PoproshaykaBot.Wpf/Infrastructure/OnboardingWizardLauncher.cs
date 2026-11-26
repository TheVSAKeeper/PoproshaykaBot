using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Views.Onboarding;
using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class OnboardingWizardLauncher(
    IServiceScopeFactory scopeFactory,
    ILogger<OnboardingWizardLauncher> logger) : IOnboardingWizardLauncher
{
    private int _isOpen;

    public event EventHandler? Closed;

    public bool HasBeenShown { get; private set; }

    public bool IsOpen => Volatile.Read(ref _isOpen) != 0;

    public void Show(OnboardingStep step = OnboardingStep.None)
    {
        if (Interlocked.CompareExchange(ref _isOpen, 1, 0) != 0)
        {
            return;
        }

        HasBeenShown = true;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var wizard = scope.ServiceProvider.GetRequiredService<OnboardingWizardWindow>();
            wizard.StartAt(step);
            wizard.ConfigureWindowPlacement(Application.Current?.MainWindow);

            if (App.IsHeadless)
            {
                OffScreenWindow.Prepare(wizard, wizard.Width, wizard.Height);
            }

            wizard.ShowDialog();
        }
        catch (Exception exception)
        {
            logger.OnboardingWizardShowFailed(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _isOpen, 0);
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}
