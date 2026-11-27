using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using Serilog;
using Serilog.Extensions.Logging;

namespace PoproshaykaBot.Wpf;

public partial class App
{
    internal static async Task<(bool OfferImport, IReadOnlyList<LegacyDataSummary> Candidates, LegacyDataSummary? Target)> ScanLegacyImportCandidatesAsync(
        ISettingsStore uiSettings,
        bool isHeadless = false)
    {
        if (uiSettings.GetBool(SettingsKeys.LegacyImportDismissed))
        {
            return (false, [], null);
        }

        using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        var logger = loggerFactory.CreateLogger(nameof(LegacyDataImporter));

        try
        {
            if (isHeadless)
            {
                var headlessTarget = LegacyDataScanner.Inspect(AppPaths.BaseDirectory, logger);
                return (headlessTarget is null, [], headlessTarget);
            }

            var (candidates, target) = await FirstRunStartViewModel.ScanAsync(logger);
            var offerImport = target is null;

            if (offerImport)
            {
                HostLog.Information("Доступен перенос данных предыдущей версии: источников – {Count}", candidates.Count);
            }

            return (offerImport, candidates, target);
        }
        catch (Exception exception)
        {
            HostLog.Error(exception, "Не удалось выполнить поиск данных предыдущей версии");
            return (false, [], null);
        }
    }

    internal static void PersistLegacyImportDismissed(ISettingsStore uiSettings)
    {
        if (!uiSettings.GetBool(SettingsKeys.LegacyImportDismissed))
        {
            uiSettings.SetBool(SettingsKeys.LegacyImportDismissed, true);
            HostLog.Information("Предложение переноса данных отключено пользователем");
        }
    }

    internal static ISettingsStore ReloadUiSettingsIfImported(
        ISettingsStore uiSettings,
        string uiSettingsPath,
        LegacyImportResult? result,
        ThemePackLoader themePackLoader)
    {
        if (result?.CopiedUiPreferences != true)
        {
            return uiSettings;
        }

        return ReopenUiSettings(uiSettings, uiSettingsPath, themePackLoader, applyThemeAndFont: true);
    }

    internal static ISettingsStore ReopenUiSettings(
        ISettingsStore uiSettings,
        string uiSettingsPath,
        ThemePackLoader themePackLoader,
        bool applyThemeAndFont)
    {
        uiSettings.Close();

        if (uiSettings is IDisposable disposable)
        {
            disposable.Dispose();
        }

        ISettingsStore reloaded = new SettingsStore(uiSettingsPath);

        if (applyThemeAndFont)
        {
            var themeKey = themePackLoader.Restore(reloaded);
            ThemeManager.Apply(themeKey ?? ThemeManager.DefaultLight.Key);
            FontScaleManager.Apply(reloaded.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));
            HostLog.Information("Настройки вида перечитаны из {Path}", uiSettingsPath);
        }

        return reloaded;
    }
}
