using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.Views.Migration;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

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

    internal static string RemapSourceAfterAppDataBackup(string sourcePath, string appDataDirectory, string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return sourcePath;
        }

        var normalizedSource = NormalizeDirectoryPath(sourcePath);
        var normalizedAppData = NormalizeDirectoryPath(appDataDirectory);

        if (string.Equals(normalizedSource, normalizedAppData, StringComparison.OrdinalIgnoreCase))
        {
            return backupPath;
        }

        var prefix = normalizedAppData + Path.DirectorySeparatorChar;
        if (normalizedSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalizedSource[prefix.Length..];
            return Path.Combine(backupPath, relative);
        }

        return sourcePath;
    }

    internal static bool TryRestoreAppDataFromBackup(
        string appDataDirectory,
        string? backupPath,
        Microsoft.Extensions.Logging.ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(appDataDirectory))
            {
                Directory.Delete(appDataDirectory, recursive: true);
            }

            Directory.Move(backupPath, appDataDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogError(exception, "Не удалось восстановить папку {AppDataDirectory} из резервной копии {BackupPath}", appDataDirectory, backupPath);
            return false;
        }
    }

    private Task<(TimeSpan ModalWait, FirstRunStartOutcome Outcome, ISettingsStore UiSettings)> ShowFirstRunStartWindowAsync(
        IReadOnlyList<LegacyDataSummary> candidates,
        ISettingsStore uiSettings,
        string uiSettingsPath,
        ThemePackLoader themePackLoader)
    {
        var currentUiSettings = uiSettings;
        var currentUiSettingsPath = uiSettingsPath;
        using var startupLoggerFactory = new SerilogLoggerFactory(Log.Logger);
        var startLogger = startupLoggerFactory.CreateLogger<FirstRunStartViewModel>();
        Mutex? pendingAppDataMutex = null;

        (Mutex? AppDataMutex, string? BackupPath) PrepareAppDataTarget(Microsoft.Extensions.Logging.ILogger? logger)
        {
            Mutex? appDataMutex = null;

            if (!IsHeadless)
            {
                appDataMutex = TryAcquireMutexForDirectory(AppPaths.AppDataDirectory, _isFinalizeUpdate);
                if (appDataMutex is null)
                {
                    throw new AppDataAlreadyRunningException(
                        "В папке %APPDATA%\\PoproshaykaBot уже запущен другой экземпляр PoproshaykaBot. Закройте его и попробуйте снова.");
                }
            }

            try
            {
                var backupPath = LegacyDataImporter.BackupAndClearAppData(AppPaths.AppDataDirectory, logger);
                return (appDataMutex, backupPath);
            }
            catch
            {
                appDataMutex?.Dispose();
                throw;
            }
        }

        void CommitAppDataSwitch(Mutex? appDataMutex, bool applyThemeAndFont)
        {
            AppPaths.SetAppDataStorageMarker(useAppData: true);

            if (appDataMutex is not null)
            {
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = appDataMutex;
            }

            _runStateStore?.ClearCleanExit();
            _previousRunState = BeginRunStateSession(AppInfo.Version, AppBuildBadge.StartupLine);

            currentUiSettingsPath = AppPaths.SettingsFile(SettingsFileNames.UiPreferencesFileName);
            currentUiSettings = ReopenUiSettings(currentUiSettings, currentUiSettingsPath, themePackLoader, applyThemeAndFont);
        }

        var startViewModel = new FirstRunStartViewModel(
            candidates,
            new FilePicker(),
            startLogger,
            import: (sourcePath, location, importLogger) =>
            {
                if (location != FirstRunStorageLocation.AppData)
                {
                    return LegacyDataImporter.Import(sourcePath, AppPaths.BaseDirectory, overwrite: false, importLogger);
                }

                var effectiveLogger = importLogger ?? startLogger;
                var (appDataMutex, backupPath) = PrepareAppDataTarget(effectiveLogger);
                try
                {
                    var effectiveSource = RemapSourceAfterAppDataBackup(sourcePath, AppPaths.AppDataDirectory, backupPath);
                    var result = LegacyDataImporter.Import(effectiveSource, AppPaths.AppDataDirectory, overwrite: false, importLogger);

                    if (!result.HasChanges && result.Failures.Count > 0)
                    {
                        appDataMutex?.Dispose();
                        if (backupPath is not null
                            && !TryRestoreAppDataFromBackup(AppPaths.AppDataDirectory, backupPath, effectiveLogger))
                        {
                            throw new AppDataBackupRestoreFailedException(
                                $"Не удалось перенести данные и автоматически вернуть исходную папку. Исходные файлы сохранены в резервной копии: {backupPath}");
                        }

                        return result;
                    }

                    pendingAppDataMutex = appDataMutex;
                    return result;
                }
                catch (AppDataBackupRestoreFailedException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    appDataMutex?.Dispose();
                    if (backupPath is not null
                        && !TryRestoreAppDataFromBackup(AppPaths.AppDataDirectory, backupPath, effectiveLogger))
                    {
                        throw new AppDataBackupRestoreFailedException(
                            $"Не удалось перенести данные и автоматически вернуть исходную папку. Исходные файлы сохранены в резервной копии: {backupPath}",
                            exception);
                    }

                    throw;
                }
            },
            onStartFresh: location =>
            {
                if (location == FirstRunStorageLocation.AppData)
                {
                    var (appDataMutex, backupPath) = PrepareAppDataTarget(startLogger);
                    try
                    {
                        CommitAppDataSwitch(appDataMutex, applyThemeAndFont: false);
                    }
                    catch
                    {
                        appDataMutex?.Dispose();
                        if (backupPath is not null)
                        {
                            TryRestoreAppDataFromBackup(AppPaths.AppDataDirectory, backupPath, startLogger);
                        }

                        throw;
                    }
                }

                PersistLegacyImportDismissed(currentUiSettings);
                return true;
            },
            afterImportAsync: (result, location) =>
            {
                if (location == FirstRunStorageLocation.AppData)
                {
                    var appDataMutex = pendingAppDataMutex;
                    pendingAppDataMutex = null;
                    CommitAppDataSwitch(appDataMutex, applyThemeAndFont: result.CopiedUiPreferences);
                    PersistLegacyImportDismissed(currentUiSettings);
                    return Task.CompletedTask;
                }

                if (!result.HasChanges)
                {
                    PersistLegacyImportDismissed(currentUiSettings);
                    return Task.CompletedTask;
                }

                currentUiSettings = ReloadUiSettingsIfImported(currentUiSettings, currentUiSettingsPath, result, themePackLoader);
                PersistLegacyImportDismissed(currentUiSettings);
                return Task.CompletedTask;
            });

        var window = new FirstRunStartWindow(startViewModel);

        if (IsHeadless)
        {
            OffScreenWindow.Prepare(window, window.Width, window.Height);
        }

        var modalStart = Stopwatch.GetTimestamp();
        window.ShowDialog();
        var modalWait = Stopwatch.GetElapsedTime(modalStart);

        pendingAppDataMutex?.Dispose();

        return Task.FromResult((modalWait, startViewModel.Outcome, currentUiSettings));
    }

    private static Mutex? TryAcquireMutexForDirectory(string directory, bool allowExistingInstance)
    {
        var key = NormalizeDirectoryPath(directory).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var mutex = new Mutex(true, $"Local\\PoproshaykaBot-{hash[..16]}", out var createdNew);

        if (createdNew || allowExistingInstance)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
