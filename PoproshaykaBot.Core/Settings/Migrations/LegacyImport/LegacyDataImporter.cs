using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Diagnostics;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

public static class LegacyDataImporter
{
    private const string BackupSuffix = "pre-import";

    private static readonly string[] AuthorizationFiles =
        [SettingsFileNames.SettingsFileName, SettingsFileNames.AccountsFileName, SettingsFileNames.ChannelsFileName];

    public static string? BackupAndClearAppData(string appDataDirectory, ILogger? logger = null)
    {
        return BackupAndClearAppData(appDataDirectory, logger, nowProvider: null);
    }

    internal static string? BackupAndClearAppData(string appDataDirectory, ILogger? logger, Func<DateTime>? nowProvider)
    {
        ArgumentException.ThrowIfNullOrEmpty(appDataDirectory);

        var fullAppData = Path.GetFullPath(appDataDirectory);
        if (!Directory.Exists(fullAppData))
        {
            Directory.CreateDirectory(fullAppData);
            return null;
        }

        if (!Directory.EnumerateFileSystemEntries(fullAppData).Any())
        {
            return null;
        }

        var parentDirectory = Path.GetDirectoryName(fullAppData)
            ?? throw new InvalidOperationException($"Не удалось определить родительский каталог для {fullAppData}");
        var folderName = Path.GetFileName(fullAppData);
        var now = nowProvider?.Invoke() ?? DateTime.Now;
        var backupBase = Path.Combine(parentDirectory, $"{folderName}.backup-{now:yyyyMMdd-HHmmss}");
        var backupPath = backupBase;
        var suffix = 2;

        while (Directory.Exists(backupPath) || File.Exists(backupPath))
        {
            backupPath = $"{backupBase}-{suffix}";
            suffix++;
        }

        Directory.Move(fullAppData, backupPath);
        Directory.CreateDirectory(fullAppData);
        logger?.LogInformation("Старые данные в {AppData} перенесены в резервную копию {BackupPath}, целевая папка очищена",
            fullAppData,
            backupPath);

        return backupPath;
    }

    public static LegacyImportResult Import(string sourcePath, bool overwrite = false, ILogger? logger = null)
    {
        return Import(sourcePath, AppPaths.BaseDirectory, overwrite, logger);
    }

    public static LegacyImportResult Import(string sourcePath, string targetBaseDirectory, bool overwrite, ILogger? logger)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return ImportCore(sourcePath, targetBaseDirectory, overwrite, logger);
        }
        finally
        {
            logger?.LogInformation("Запись данных предыдущей версии заняла {ElapsedMs:F0} мс", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static LegacyImportResult ImportCore(string sourcePath, string targetBaseDirectory, bool overwrite, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(targetBaseDirectory);

        var copied = new List<string>();
        var skipped = new List<LegacyImportSkip>();
        var failures = new List<LegacyImportFailure>();

        string fullSource;
        string fullTarget;

        try
        {
            fullSource = Path.GetFullPath(sourcePath);
            fullTarget = Path.GetFullPath(targetBaseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            logger?.LogError(exception, "Импорт данных: путь {Path} не удалось разобрать", sourcePath);
            failures.Add(new(string.Empty, $"Не удалось разобрать путь: {sourcePath}"));
            return Build(sourcePath, targetBaseDirectory, copied, skipped, failures, false, false, false, [], []);
        }

        if (!Directory.Exists(fullSource))
        {
            logger?.LogWarning("Импорт данных: папка-источник {Source} недоступна – переносить нечего", fullSource);
            failures.Add(new(string.Empty, $"Папка-источник недоступна: {fullSource}"));
            return Build(fullSource, fullTarget, copied, skipped, failures, false, false, false, [], []);
        }

        var settingsDirectory = Path.Combine(fullTarget, SettingsFileNames.SettingsFolderName);
        var sourceAuthorization = LegacyDataReader.ReadTwitchAuthorization(fullSource, logger);
        var targetAuthorizationBefore = LegacyDataReader.ReadTwitchAuthorization(fullTarget, logger);
        var inPlace = PathComparison.AreEqual(fullSource, fullTarget);
        var preExistingSettingsFiles = inPlace || overwrite || !Directory.Exists(settingsDirectory)
            ? null
            : Directory.EnumerateFiles(settingsDirectory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (inPlace)
        {
            // Перенос на месте сохраняет действующие файлы настроек, конфликты входят в итог.
            logger?.LogInformation("Импорт данных: источник совпадает с рабочей папкой {Target} – копирование не требуется, выполняется только миграция раскладки",
                fullTarget);
        }
        else
        {
            Directory.CreateDirectory(fullTarget);
            CopyAll(fullSource, fullTarget, overwrite, copied, skipped, failures, logger);
            ResetStaleUserCatalogAfterLegacyOverwrite(fullSource, fullTarget, overwrite, copied, failures, logger);
        }

        Func<string, bool>? canOverwriteSplitTarget = inPlace || overwrite
            ? null
            : fileName => preExistingSettingsFiles is null || !preExistingSettingsFiles.Contains(fileName);

        var migratedSettingsFiles = new List<string>(LegacySettingsLayoutMigrator.Run(
            fullTarget,
            settingsDirectory,
            logger,
            failures.Add,
            overwriteExistingTargets: inPlace || overwrite,
            reportSkippedTarget: fileName => skipped.Add(new(
                Path.Combine(SettingsFileNames.SettingsFolderName, fileName),
                LegacyImportSkipReason.TargetExists)),
            canOverwriteTarget: canOverwriteSplitTarget));

        var leftovers = CollectUnmigratedLegacyFiles(fullTarget, failures, logger);

        var authorizationTouched = copied.Select(Path.GetFileName).Concat(migratedSettingsFiles)
            .Any(file => AuthorizationFiles.Contains(file, StringComparer.OrdinalIgnoreCase));
        var targetAuthorization = authorizationTouched ? LegacyDataReader.ReadTwitchAuthorization(fullTarget, logger) : default;
        var botAuthorizationRequired = authorizationTouched && !targetAuthorization.BotReady;
        var broadcasterAuthorizationRecommended = authorizationTouched && !targetAuthorization.BroadcasterReady
            && (sourceAuthorization.HasBroadcasterCredentials || targetAuthorizationBefore.HasBroadcasterCredentials
                || targetAuthorization.HasBroadcasterCredentials);

        logger?.LogInformation("Импорт данных завершён: скопировано {Copied}, пропущено {Skipped}, с ошибками {Failed}",
            copied.Count,
            skipped.Count,
            failures.Count);

        if (botAuthorizationRequired || broadcasterAuthorizationRecommended)
        {
            logger?.LogWarning("Импорт данных: требуется авторизация Twitch – бот: {Bot}, стример (необязательно): {Broadcaster}",
                botAuthorizationRequired, broadcasterAuthorizationRecommended);
        }

        return Build(fullSource, fullTarget, copied, skipped, failures, botAuthorizationRequired,
            broadcasterAuthorizationRecommended, inPlace, leftovers, migratedSettingsFiles);
    }

    internal static void CopyAll(
        string source,
        string target,
        bool overwrite,
        List<string> copied,
        List<LegacyImportSkip> skipped,
        List<LegacyImportFailure> failures,
        ILogger? logger)
    {
        var files = LegacyDataCatalog.Enumerate(source);

        if (files.Count == 0 && !Directory.Exists(source))
        {
            logger?.LogError("Импорт данных: папка-источник {Source} стала недоступна во время переноса", source);
            failures.Add(new(string.Empty, $"Папка-источник стала недоступна во время переноса: {source}"));
            return;
        }

        foreach (var file in files)
        {
            var targetPath = Path.Combine(target, file.RelativeTargetPath);

            if (PathComparison.AreEqual(file.SourcePath, targetPath))
            {
                skipped.Add(new(file.RelativeTargetPath, LegacyImportSkipReason.SameLocation));

                logger?.LogWarning("Импорт данных: {File} уже лежит на своём месте – копирование пропущено",
                    file.RelativeTargetPath);

                continue;
            }

            if (File.Exists(targetPath) && !overwrite)
            {
                skipped.Add(new(file.RelativeTargetPath, LegacyImportSkipReason.TargetExists));

                logger?.LogWarning("Импорт данных: {File} пропущен – в рабочей папке уже есть такой файл, он оставлен без изменений",
                    file.RelativeTargetPath);

                continue;
            }

            try
            {
                if (File.Exists(targetPath) && !TryBackup(targetPath, file.Name, logger))
                {
                    failures.Add(new(file.RelativeTargetPath, "Резервная копия не создана, файл оставлен без изменений"));

                    logger?.LogError("Импорт данных: {File} не перезаписан – не удалось создать резервную копию прежнего файла",
                        file.RelativeTargetPath);

                    continue;
                }

                CopyAtomic(file.SourcePath, targetPath, file.Name, file.RelativeTargetPath, logger);
                copied.Add(file.RelativeTargetPath);

                logger?.LogInformation("Импорт данных: {Source} перенесён в {Target}", file.SourcePath, targetPath);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Импорт данных: не удалось перенести {Source} в {Target}", file.SourcePath, targetPath);
                failures.Add(new(file.RelativeTargetPath, exception.Message));
            }
        }
    }

    private static void ResetStaleUserCatalogAfterLegacyOverwrite(
        string fullSource,
        string fullTarget,
        bool overwrite,
        List<string> copied,
        List<LegacyImportFailure> failures,
        ILogger? logger)
    {
        if (!overwrite)
        {
            return;
        }

        var sourceCatalogPath = Path.Combine(fullSource, SettingsFileNames.UserStatisticsCatalogFileName);
        var sourceHasCatalog = File.Exists(sourceCatalogPath);
        var copiedCatalog = copied.Contains(SettingsFileNames.UserStatisticsCatalogFileName, StringComparer.OrdinalIgnoreCase);
        var failedCatalog = failures.Any(f => string.Equals(
            LegacyDataCatalog.NormalizeRelativePath(f.RelativeTargetPath),
            SettingsFileNames.UserStatisticsCatalogFileName,
            StringComparison.OrdinalIgnoreCase));
        var copiedLegacyUsers = LegacyDataCatalog.GetRelativePaths(LegacyDataCategory.UserStatistics)
            .Where(path => !string.Equals(path, SettingsFileNames.UserStatisticsCatalogFileName, StringComparison.OrdinalIgnoreCase))
            .Any(path => copied.Any(c => string.Equals(LegacyDataCatalog.NormalizeRelativePath(c), path, StringComparison.OrdinalIgnoreCase)));

        if (sourceHasCatalog || copiedCatalog || failedCatalog || !copiedLegacyUsers)
        {
            return;
        }

        var targetCatalogPath = Path.Combine(fullTarget, SettingsFileNames.UserStatisticsCatalogFileName);
        if (!File.Exists(targetCatalogPath))
        {
            return;
        }

        if (!TryBackup(targetCatalogPath, SettingsFileNames.UserStatisticsCatalogFileName, logger))
        {
            failures.Add(new(SettingsFileNames.UserStatisticsCatalogFileName, "Резервная копия не создана, каталог статистики оставлен без изменений"));
            logger?.LogError("Импорт данных: не удалось создать резервную копию {File} при импорте v1-статистики", targetCatalogPath);
            return;
        }

        try
        {
            File.Delete(targetCatalogPath);
            copied.Add(SettingsFileNames.UserStatisticsCatalogFileName);
            logger?.LogInformation("Импорт данных: прежний каталог {File} снят после перезаписи v1-статистики для повторной миграции", targetCatalogPath);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Импорт данных: не удалось удалить прежний каталог {File}", targetCatalogPath);
            failures.Add(new(SettingsFileNames.UserStatisticsCatalogFileName, exception.Message));
        }
    }

    private static bool TryBackup(string targetPath, string fileName, ILogger? logger)
    {
        return JsonStoreBackup.TryCreateBackup(targetPath, BackupSuffix, logger, ResolveRedactor(fileName), out _);
    }

    private static IReadOnlyList<string> CollectUnmigratedLegacyFiles(
        string target, IReadOnlyList<LegacyImportFailure> failures, ILogger? logger)
    {
        var leftovers = LegacyDataCatalog.SettingsFiles
            .Where(name => File.Exists(Path.Combine(target, name))
                && File.Exists(Path.Combine(target, SettingsFileNames.SettingsFolderName, name))
                && !failures.Any(failure => failure.DestinationWritten
                    && string.Equals(failure.RelativeTargetPath, name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (leftovers.Count > 0)
        {
            logger?.LogWarning("Импорт данных: в корне рабочей папки остались файлы старой раскладки ({Files}) – в settings/ уже есть свои версии, поэтому они не перенесены",
                string.Join(", ", leftovers));
        }

        return leftovers;
    }

    private static void CopyAtomic(string source, string target, string fileName, string relativeTargetPath, ILogger? logger)
    {
        AtomicFile.Save(target,
            temporaryPath => File.Copy(source, temporaryPath, true),
            logger,
            keepBackup: !LegacyDataCatalog.CarriesTokens(fileName) && !LegacyDataCatalog.IsChatHistoryPath(relativeTargetPath));
    }

    private static Func<string, string>? ResolveRedactor(string fileName)
    {
        return LegacyDataCatalog.CarriesTokens(fileName) ? AccountsTokenRedactor.Redact : null;
    }

    private static LegacyImportResult Build(
        string source,
        string target,
        List<string> copied,
        List<LegacyImportSkip> skipped,
        List<LegacyImportFailure> failures,
        bool botAuthorizationRequired,
        bool broadcasterAuthorizationRecommended,
        bool inPlace,
        IReadOnlyList<string> unmigratedLegacyFiles,
        IReadOnlyList<string> migratedSettingsFiles)
    {
        return new()
        {
            SourcePath = source,
            TargetPath = target,
            CopiedFiles = copied,
            SkippedFiles = skipped,
            Failures = failures,
            BotAuthorizationRequired = botAuthorizationRequired,
            BroadcasterAuthorizationRecommended = broadcasterAuthorizationRecommended,
            IsInPlaceMigration = inPlace,
            UnmigratedLegacyFiles = unmigratedLegacyFiles,
            MigratedSettingsFiles = migratedSettingsFiles,
        };
    }
}
