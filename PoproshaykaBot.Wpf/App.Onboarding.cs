using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.Views.Onboarding;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    internal sealed record StartupOnboardingPlan(bool ShouldShow, OnboardingStep InitialStep, bool IncludeLegacyImport);

    internal static StartupOnboardingPlan GetStartupOnboardingPlan(OnboardingChecklist checklist, bool hasImportOffer)
    {
        if (hasImportOffer)
        {
            return new(true, OnboardingStep.None, IncludeLegacyImport: true);
        }

        if (checklist.RequiresWizard)
        {
            return new(true, OnboardingStep.None, IncludeLegacyImport: false);
        }

        return new(false, OnboardingStep.None, IncludeLegacyImport: false);
    }

    private async Task<(TimeSpan ModalWait, ISettingsStore UiSettings)> ShowStartupOnboardingWizardAsync(
        StartupOnboardingPlan plan,
        IReadOnlyList<LegacyDataSummary> candidates,
        LegacyDataSummary? target,
        ISettingsStore uiSettings,
        string uiSettingsPath,
        ThemePackLoader themePackLoader,
        UiLogSink coreUiLogSink)
    {
        if (_services is null)
        {
            return (TimeSpan.Zero, uiSettings);
        }

        var pageScope = _services.CreateScope();
        OnboardingWizardViewModel? wizardViewModel = null;

        try
        {
            using var startupLoggerFactory = new SerilogLoggerFactory(Log.Logger);
            var pages = new List<IOnboardingPageViewModel>();

            foreach (var page in pageScope.ServiceProvider.GetServices<IOnboardingPageViewModel>())
            {
                if (page is WelcomePageViewModel or LegacyImportPageViewModel)
                {
                    if (page is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    continue;
                }

                pages.Add(page);
            }

            pages.Insert(0, new WelcomePageViewModel());
            pages.Insert(1, new LegacyImportPageViewModel(
                new FilePicker(),
                startupLoggerFactory.CreateLogger<LegacyImportPageViewModel>()));

            wizardViewModel = new OnboardingWizardViewModel(
                pages,
                _services.GetRequiredService<SettingsManager>(),
                _services.GetRequiredService<AccountsStore>(),
                _services.GetRequiredService<KestrelHttpServer>(),
                _services.GetRequiredService<BotConnectionManager>(),
                _services.GetRequiredService<IEventBus>(),
                _services.GetRequiredService<ILogger<OnboardingWizardViewModel>>(),
                _services.GetService<YouTubeSettingsStore>(),
                _services.GetService<YouTubeAccountsStore>(),
                _services.GetService<ChannelCatalogStore>(),
                _services.GetService<IDialogService>(),
                _services.GetService<OnboardingChecklist>());

            var currentUiSettings = uiSettings;

            wizardViewModel.AttachStartupSession(new StartupOnboardingSession
            {
                IncludeLegacyImport = plan.IncludeLegacyImport,
                Candidates = candidates,
                Target = target,
                OnLegacyImportSkipped = neverAskAgain =>
                {
                    if (neverAskAgain)
                    {
                        PersistLegacyImportDismissed(currentUiSettings);
                    }
                },
                OnImportCompletedAsync = async (result, neverAskAgain) =>
                {
                    if (neverAskAgain)
                    {
                        PersistLegacyImportDismissed(currentUiSettings);
                    }

                    if (!result.HasChanges)
                    {
                        return null;
                    }

                    if (result.CopiedUiPreferences)
                    {
                        wizardViewModel.DiscardAppearanceRollback();
                    }

                    currentUiSettings = ReloadUiSettingsIfImported(currentUiSettings, uiSettingsPath, result, themePackLoader);
                    await Task.Run(MigrateLegacySettingsLayout);

                    var previousServices = _services;
                    var previousScope = pageScope;

                    var rebuiltServices = BuildAndInitializeServices(currentUiSettings, coreUiLogSink, themePackLoader);
                    var rebuiltScope = rebuiltServices.CreateScope();

                    _services = rebuiltServices;
                    pageScope = rebuiltScope;

                    wizardViewModel.PrepareForContainerReload();

                    if (previousServices?.GetService<KestrelHttpServer>() is { IsRunning: true } previousServer)
                    {
                        try
                        {
                            await previousServer.StopAsync();
                        }
                        catch (Exception ex)
                        {
                            HostLog.Error(ex, "Ошибка остановки HTTP сервера");
                        }
                    }

                    previousScope.Dispose();

                    if (previousServices is not null)
                    {
                        await previousServices.DisposeAsync();
                    }

                    return pageScope.ServiceProvider;
                },
            });

            var wizard = new OnboardingWizardWindow(wizardViewModel);
            wizard.StartAt(plan.InitialStep);
            wizard.ConfigureWindowPlacement(owner: null);

            if (IsHeadless)
            {
                OffScreenWindow.Prepare(wizard, wizard.Width, wizard.Height);
            }

            var modalStart = Stopwatch.GetTimestamp();
            wizard.ShowDialog();
            var modalWait = Stopwatch.GetElapsedTime(modalStart);

            return (modalWait, currentUiSettings);
        }
        finally
        {
            wizardViewModel?.Dispose();
            pageScope.Dispose();
        }
    }
}
