using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    private bool _onboardingLaunched;

    internal static OnboardingStep? GetStartupWizardStep(OnboardingChecklist checklist, LegacyImportResult? import)
    {
        if (import?.HasChanges == true)
        {
            var step = checklist.FirstMissingStep;
            return step != OnboardingStep.None ? step : null;
        }

        return checklist.RequiresWizard ? OnboardingStep.None : null;
    }

    private void MaybeLaunchOnboardingWizard(LegacyImportResult? import)
    {
        if (_onboardingLaunched || _services is null)
        {
            return;
        }

        var checklist = _services.GetRequiredService<OnboardingChecklist>();

        if (GetStartupWizardStep(checklist, import) is not { } step)
        {
            return;
        }

        _onboardingLaunched = true;

        _ = Dispatcher.BeginInvoke(() => ShowStartupWizard(step), DispatcherPriority.ContextIdle);
    }

    private void ShowStartupWizard(OnboardingStep step)
    {
        var launcher = _services!.GetRequiredService<IOnboardingWizardLauncher>();

        if (launcher.HasBeenShown)
        {
            return;
        }

        launcher.Show(step);
    }
}
