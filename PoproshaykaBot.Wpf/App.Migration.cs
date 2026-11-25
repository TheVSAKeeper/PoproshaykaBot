using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.Views.Migration;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.IO;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    private static async Task<(ISettingsStore Settings, TimeSpan ModalWait, LegacyImportResult? Result)> OfferLegacyDataImportAsync(
        ISettingsStore uiSettings,
        string uiSettingsPath,
        ThemePackLoader themePackLoader)
    {
        if (uiSettings.GetBool(SettingsKeys.LegacyImportDismissed))
        {
            return (uiSettings, TimeSpan.Zero, null);
        }

        var modalWait = TimeSpan.Zero;
        LegacyImportResult? result = null;
        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(LegacyDataImporter));

        try
        {
            var (candidates, target) = await LegacyImportViewModel.ScanAsync(logger);

            if (candidates.Count == 0 && target is not null)
            {
                return (uiSettings, modalWait, result);
            }

            var viewModel = new LegacyImportViewModel(candidates, target, isSettingsEntry: false, new FilePicker(), logger);
            var window = new LegacyImportWindow(viewModel);

            HostLog.Information("Предложен перенос данных предыдущей версии: источников – {Count}", candidates.Count);

            var modalStart = Stopwatch.GetTimestamp();
            window.ShowDialog();
            modalWait = Stopwatch.GetElapsedTime(modalStart);
            result = viewModel.Result;

            if (result is null)
            {
                HostLog.Information("Пользователь отказался от переноса данных предыдущей версии");
            }

            if (viewModel.NeverAskAgain)
            {
                uiSettings.SetBool(SettingsKeys.LegacyImportDismissed, true);
                HostLog.Information("Предложение переноса данных отключено пользователем");
            }

            return (ReloadUiSettingsIfImported(uiSettings, uiSettingsPath, result, themePackLoader), modalWait, result);
        }
        catch (Exception exception)
        {
            HostLog.Error(exception, "Диалог переноса данных предыдущей версии не открылся");
            return (uiSettings, modalWait, result);
        }
    }

    private static ISettingsStore ReloadUiSettingsIfImported(
        ISettingsStore uiSettings,
        string uiSettingsPath,
        LegacyImportResult? result,
        ThemePackLoader themePackLoader)
    {
        if (result?.CopiedUiPreferences != true)
        {
            return uiSettings;
        }

        uiSettings.Close();

        if (uiSettings is IDisposable disposable)
        {
            disposable.Dispose();
        }

        ISettingsStore reloaded = new SettingsStore(uiSettingsPath);

        var themeKey = themePackLoader.Restore(reloaded);
        ThemeManager.Apply(themeKey ?? ThemeManager.DefaultLight.Key);
        FontScaleManager.Apply(reloaded.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));

        HostLog.Information("Перенесённые настройки вида перечитаны из {Path}", uiSettingsPath);

        return reloaded;
    }
}
