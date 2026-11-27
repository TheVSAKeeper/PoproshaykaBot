using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Onboarding;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.Views.Migration;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    internal static bool ShouldShowFirstRunStart(
        LegacyDataSummary? target,
        ISettingsStore uiSettings,
        bool isGallery)
    {
        return !isGallery
            && target is null
            && !uiSettings.GetBool(SettingsKeys.LegacyImportDismissed);
    }

    private async Task<(TimeSpan ModalWait, FirstRunStartOutcome Outcome, ISettingsStore UiSettings)> ShowFirstRunStartWindowAsync(
        IReadOnlyList<LegacyDataSummary> candidates,
        ISettingsStore uiSettings,
        string uiSettingsPath,
        ThemePackLoader themePackLoader,
        UiLogSink coreUiLogSink)
    {
        if (_services is null)
        {
            return (TimeSpan.Zero, FirstRunStartOutcome.None, uiSettings);
        }

        var currentUiSettings = uiSettings;
        using var startupLoggerFactory = new SerilogLoggerFactory(Log.Logger);

        var startViewModel = new FirstRunStartViewModel(
            candidates,
            new FilePicker(),
            startupLoggerFactory.CreateLogger<FirstRunStartViewModel>(),
            afterImportAsync: async result =>
            {
                if (!result.HasChanges)
                {
                    PersistLegacyImportDismissed(currentUiSettings);
                    return;
                }

                currentUiSettings = ReloadUiSettingsIfImported(currentUiSettings, uiSettingsPath, result, themePackLoader);
                PersistLegacyImportDismissed(currentUiSettings);

                await Task.Run(MigrateLegacySettingsLayout);

                var previousServices = _services;
                var rebuiltServices = BuildAndInitializeServices(currentUiSettings, coreUiLogSink, themePackLoader);
                _services = rebuiltServices;

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

                if (previousServices is not null)
                {
                    await previousServices.DisposeAsync();
                }
            },
            onStartFresh: () => PersistLegacyImportDismissed(currentUiSettings),
            isTwitchEnabled: () => _services?.GetService<SettingsManager>()?.Current.Twitch.Enabled ?? true,
            isTwitchBotAuthMissing: () => _services?.GetService<OnboardingChecklist>()?.HasMissingTwitchBotAuthorization ?? false);

        var window = new FirstRunStartWindow(startViewModel);

        if (IsHeadless)
        {
            OffScreenWindow.Prepare(window, window.Width, window.Height);
        }

        var modalStart = Stopwatch.GetTimestamp();
        window.ShowDialog();
        var modalWait = Stopwatch.GetElapsedTime(modalStart);

        return (modalWait, startViewModel.Outcome, currentUiSettings);
    }
}
