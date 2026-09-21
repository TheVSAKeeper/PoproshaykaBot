using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Settings.Migrations;

public static class LegacySettingsLayoutMigrator
{
    private static readonly string[] KnownSettingsFiles =
    [
        "settings.json",
        "accounts.json",
        "broadcast-profiles.json",
        "polls.json",
        "obs-chat.json",
        "obs-integration.json",
        "recent-categories.json",
        "dashboard-layout.json",
    ];

    public static IReadOnlyList<string> Run(string baseDirectory, string settingsDirectory, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        ArgumentException.ThrowIfNullOrEmpty(settingsDirectory);

        if (PathComparison.AreEqual(baseDirectory, settingsDirectory))
        {
            return [];
        }

        if (!Directory.Exists(baseDirectory))
        {
            return [];
        }

        Directory.CreateDirectory(settingsDirectory);
        var relocated = RelocateFlatLayout(baseDirectory, settingsDirectory, logger);
        var split = SplitMonolithicSettings(settingsDirectory, logger);

        return [.. relocated.Union(split, StringComparer.OrdinalIgnoreCase)];
    }

    private static List<string> RelocateFlatLayout(string baseDirectory, string settingsDirectory, ILogger? logger)
    {
        var relocated = new List<string>();

        foreach (var fileName in KnownSettingsFiles)
        {
            var legacy = Path.Combine(baseDirectory, fileName);
            var target = Path.Combine(settingsDirectory, fileName);

            if (!File.Exists(legacy))
            {
                continue;
            }

            if (File.Exists(target))
            {
                logger?.LogWarning("Legacy-файл {Legacy} оставлен на месте: целевой {Target} уже существует",
                    legacy,
                    target);

                continue;
            }

            try
            {
                AtomicFile.Save(target, temporaryPath => File.Copy(legacy, temporaryPath, true), logger);
                var backupPath = BuildLegacyBackupPath(legacy);
                File.Move(legacy, backupPath);
                relocated.Add(fileName);
                logger?.LogInformation("Legacy-файл перенесён: {Legacy} → {Target}; оригинал сохранён как {Backup}",
                    legacy,
                    target,
                    backupPath);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Не удалось перенести {Legacy} в {Target}", legacy, target);
            }
        }

        return relocated;
    }

    private static string BuildLegacyBackupPath(string legacyPath)
    {
        var directory = Path.GetDirectoryName(legacyPath)!;
        var name = Path.GetFileNameWithoutExtension(legacyPath);
        var extension = Path.GetExtension(legacyPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(directory, $"{name}.legacy-{timestamp}{extension}");
    }

    private static IReadOnlyList<string> SplitMonolithicSettings(string settingsDirectory, ILogger? logger)
    {
        var settingsFile = Path.Combine(settingsDirectory, "settings.json");

        if (!File.Exists(settingsFile))
        {
            return [];
        }

        var migration = SettingsMigrationResult.Unchanged;

        try
        {
            var json = File.ReadAllText(settingsFile, Encoding.UTF8);

            if (JsonNode.Parse(json) is not JsonObject root)
            {
                return [];
            }

            migration = SettingsMigrator.Migrate(root, logger, settingsDirectory);

            if (!migration.Changed)
            {
                return migration.SplitFiles;
            }

            JsonStoreBackup.CreateBackup(settingsFile, "pre-migration", logger, AccountsTokenRedactor.Redact);
            AtomicFile.Save(settingsFile, root.ToJsonString(JsonStoreOptions.Default), logger);
            logger?.LogInformation("Монолитный settings.json мигрирован и разбит на отдельные файлы (директория {Directory})",
                settingsDirectory);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Ошибка миграции монолитного settings.json в {Directory}", settingsDirectory);
        }

        return migration.SplitFiles;
    }
}
