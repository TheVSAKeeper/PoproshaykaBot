using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Wpf.Views.Onboarding;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    private bool _onboardingLaunched;

    private void MaybeLaunchOnboardingWizard()
    {
        if (_onboardingLaunched || _services is null)
        {
            return;
        }

        var checklist = _services.GetRequiredService<OnboardingChecklist>();

        if (!checklist.RequiresWizard)
        {
            return;
        }

        _onboardingLaunched = true;

        Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var wizard = _services!.GetRequiredService<OnboardingWizardWindow>();
                    wizard.Owner = MainWindow;
                    wizard.ShowDialog();
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Ошибка показа мастера первичной настройки");
                }
            },
            DispatcherPriority.ContextIdle);
    }
}
