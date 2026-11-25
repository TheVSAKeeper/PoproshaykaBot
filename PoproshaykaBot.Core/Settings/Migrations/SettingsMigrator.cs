using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Settings.Migrations;

public static class SettingsMigrator
{
    private const string BotAccountKey = "botAccount";
    private const string BroadcasterAccountKey = "broadcasterAccount";

    public static SettingsMigrationResult Migrate(JsonObject root, ILogger? logger = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var changed = false;
        var splitFiles = new List<string>();

        if (root["twitch"] is JsonObject twitch)
        {
            changed |= MigrateBotAccount(twitch, logger);
        }

        changed |= RemoveLegacyUiFields(root, logger);

        if (baseDirectory != null)
        {
            changed |= SplitMonolithicSettings(root, (fileName, content, redact) =>
                TryWriteSplit(Path.Combine(baseDirectory, fileName), content.ToJsonString(JsonStoreOptions.Default), fileName, splitFiles, logger, redact));
        }

        return new(changed, splitFiles);
    }

    internal static IReadOnlyList<string> GetSplitTargets(JsonObject root)
    {
        var draft = (JsonObject)root.DeepClone();
        if (draft["twitch"] is JsonObject twitch)
        {
            MigrateBotAccount(twitch, null);
        }

        var targets = new List<string>();
        SplitMonolithicSettings(draft, (name, _, _) =>
        {
            targets.Add(name);
            return true;
        });
        return targets;
    }

    private delegate bool WriteSplit(string fileName, JsonObject value, Func<string, string>? backupRedactor = null);

    private static bool SplitMonolithicSettings(JsonObject root, WriteSplit write)
    {
        var changed = false;

        if (root["twitch"] is JsonObject twitch)
        {
            changed |= SplitAccounts(twitch, write);
            changed |= TrySplitObject(twitch, "broadcastProfiles", "broadcast-profiles.json", write);
            changed |= TrySplitObject(twitch, "polls", "polls.json", write);
            changed |= SplitRecentCategories(twitch, write);
            changed |= TrySplitObject(twitch, "obsChat", "obs-chat.json", write);
        }

        changed |= SplitDashboardLayout(root, write);

        return changed;
    }

    private static bool SplitAccounts(JsonObject twitch, WriteSplit write)
    {
        var botAccount = twitch[BotAccountKey] as JsonObject;
        var broadcasterAccount = twitch[BroadcasterAccountKey] as JsonObject;

        if (botAccount == null && broadcasterAccount == null)
        {
            return false;
        }

        var dto = new JsonObject
        {
            [BotAccountKey] = botAccount?.DeepClone() ?? new JsonObject(),
            [BroadcasterAccountKey] = broadcasterAccount?.DeepClone() ?? new JsonObject(),
        };

        if (!write("accounts.json", dto, AccountsTokenRedactor.Redact))
        {
            return false;
        }

        var changed = false;
        changed |= twitch.Remove(BotAccountKey);
        changed |= twitch.Remove(BroadcasterAccountKey);
        return changed;
    }

    private static bool SplitRecentCategories(JsonObject twitch, WriteSplit write)
    {
        if (twitch["infrastructure"] is not JsonObject infrastructure
            || infrastructure["recentCategories"] is not JsonArray recentCategories)
        {
            return false;
        }

        var dto = new JsonObject
        {
            ["items"] = recentCategories.DeepClone(),
        };

        return write("recent-categories.json", dto)
               && infrastructure.Remove("recentCategories");
    }

    private static bool SplitDashboardLayout(JsonObject root, WriteSplit write)
    {
        if (root["ui"] is not JsonObject ui
            || !ui.ContainsKey("dashboard") && !ui.ContainsKey("mainWindow"))
        {
            return false;
        }

        var dto = new JsonObject
        {
            ["dashboard"] = ui["dashboard"]?.DeepClone(),
            ["mainWindow"] = ui["mainWindow"]?.DeepClone(),
        };

        return write("dashboard-layout.json", dto)
               && root.Remove("ui");
    }

    private static bool TrySplitObject(
        JsonObject parent,
        string key,
        string targetFileName,
        WriteSplit write)
    {
        if (parent[key] is not JsonObject value)
        {
            return false;
        }

        if (!write(targetFileName, value))
        {
            return false;
        }

        return parent.Remove(key);
    }

    private static bool TryWriteSplit(
        string target,
        string content,
        string targetFileName,
        List<string> splitFiles,
        ILogger? logger,
        Func<string, string>? backupRedactor = null)
    {
        try
        {
            if (File.Exists(target))
            {
                JsonStoreBackup.CreateBackup(target, "pre-migration", logger, backupRedactor);
                logger?.LogWarning("Миграция настроек: целевой {TargetFileName} уже существовал – создан бэкап перед перезаписью авторитетным источником из settings.json",
                    targetFileName);
            }

            AtomicFile.Save(target, content, logger, keepBackup: backupRedactor == null);
            splitFiles.Add(targetFileName);
            logger?.LogInformation("Миграция настроек: данные из settings.json вынесены в {TargetFileName}",
                targetFileName);

            return true;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception,
                "Миграция настроек: не удалось записать {TargetFileName} – легаси-поле в settings.json оставлено без изменений",
                targetFileName);

            return false;
        }
    }

    private static bool MigrateBotAccount(JsonObject twitch, ILogger? logger)
    {
        var changed = false;

        var botAccount = EnsureObject(twitch, BotAccountKey);

        changed |= MoveStringIfMissing(twitch, "botUsername", botAccount, "login", logger, "botUsername → botAccount.login");
        changed |= RemoveIfPresent(twitch, "accessToken", logger, "twitch.accessToken удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "refreshToken", logger, "twitch.refreshToken удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "storedScopes", logger, "twitch.storedScopes удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "scopes", logger, "twitch.scopes удалён (требуется новый набор прав)");

        if (botAccount.Count == 0 && twitch[BotAccountKey] is JsonObject existing && ReferenceEquals(existing, botAccount))
        {
            twitch.Remove(BotAccountKey);
        }

        return changed;
    }

    private static bool RemoveLegacyUiFields(JsonObject root, ILogger? logger)
    {
        if (root["ui"] is not JsonObject ui)
        {
            return false;
        }

        var changed = false;
        changed |= RemoveIfPresent(ui, "showLogsPanel", logger, "ui.showLogsPanel удалён (заменён дашбордом)");
        changed |= RemoveIfPresent(ui, "showChatPanel", logger, "ui.showChatPanel удалён (заменён дашбордом)");
        changed |= RemoveIfPresent(ui, "currentChatViewMode", logger, "ui.currentChatViewMode удалён (legacy-режим чата убран)");
        return changed;
    }

    private static JsonObject EnsureObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    private static bool MoveStringIfMissing(JsonObject source, string sourceKey, JsonObject target, string targetKey, ILogger? logger, string description)
    {
        if (!source.ContainsKey(sourceKey))
        {
            return false;
        }

        if (source[sourceKey] is not JsonValue value || !value.TryGetValue(out string? text))
        {
            source.Remove(sourceKey);
            return true;
        }

        if (HasNonEmptyString(target, targetKey) || string.IsNullOrEmpty(text))
        {
            source.Remove(sourceKey);
            return true;
        }

        target[targetKey] = text;
        source.Remove(sourceKey);
        logger?.LogInformation("Миграция настроек: {Description}", description);
        return true;
    }

    private static bool RemoveIfPresent(JsonObject parent, string key, ILogger? logger, string description)
    {
        if (!parent.ContainsKey(key))
        {
            return false;
        }

        parent.Remove(key);
        logger?.LogInformation("Миграция настроек: {Description}", description);
        return true;
    }

    private static bool HasNonEmptyString(JsonObject parent, string key)
    {
        return parent[key] is JsonValue value
               && value.TryGetValue(out string? text)
               && !string.IsNullOrEmpty(text);
    }
}
