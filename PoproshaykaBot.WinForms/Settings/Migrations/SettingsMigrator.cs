using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings.Stores;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.WinForms.Settings.Migrations;

public static class SettingsMigrator
{
    public static bool TryMigrate(JsonObject root, ILogger? logger = null, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var changed = false;

        if (root["twitch"] is JsonObject twitch)
        {
            changed |= MigrateBotAccount(twitch, logger);
        }

        changed |= RemoveLegacyUiFields(root, logger);

        if (baseDirectory != null)
        {
            changed |= SplitMonolithicSettings(root, baseDirectory, logger);
        }

        return changed;
    }

    private static bool SplitMonolithicSettings(JsonObject root, string baseDirectory, ILogger? logger)
    {
        var changed = false;

        if (root["twitch"] is JsonObject twitch)
        {
            // accounts.json
            var botAccount = twitch["botAccount"] as JsonObject;
            var broadcasterAccount = twitch["broadcasterAccount"] as JsonObject;

            if (botAccount != null || broadcasterAccount != null)
            {
                var accountsTarget = Path.Combine(baseDirectory, "accounts.json");

                if (!File.Exists(accountsTarget))
                {
                    var dto = new JsonObject
                    {
                        ["botAccount"] = botAccount?.DeepClone() ?? new JsonObject(),
                        ["broadcasterAccount"] = broadcasterAccount?.DeepClone() ?? new JsonObject(),
                    };

                    AtomicFile.Save(accountsTarget, dto.ToJsonString(JsonStoreOptions.Default), logger);
                    logger?.LogInformation("Миграция настроек: вынесен botAccount/broadcasterAccount в accounts.json");
                }

                if (twitch.Remove("botAccount"))
                {
                    changed = true;
                }

                if (twitch.Remove("broadcasterAccount"))
                {
                    changed = true;
                }
            }

            // broadcast-profiles.json
            if (twitch["broadcastProfiles"] is JsonObject broadcastProfiles)
            {
                var target = Path.Combine(baseDirectory, "broadcast-profiles.json");

                if (!File.Exists(target))
                {
                    AtomicFile.Save(target, broadcastProfiles.DeepClone()!.ToJsonString(JsonStoreOptions.Default), logger);
                    logger?.LogInformation("Миграция настроек: вынесен broadcastProfiles в broadcast-profiles.json");
                }

                twitch.Remove("broadcastProfiles");
                changed = true;
            }

            // polls.json
            if (twitch["polls"] is JsonObject polls)
            {
                var target = Path.Combine(baseDirectory, "polls.json");

                if (!File.Exists(target))
                {
                    AtomicFile.Save(target, polls.DeepClone()!.ToJsonString(JsonStoreOptions.Default), logger);
                    logger?.LogInformation("Миграция настроек: вынесен polls в polls.json");
                }

                twitch.Remove("polls");
                changed = true;
            }

            // recent-categories.json
            if (twitch["infrastructure"] is JsonObject infrastructure
                && infrastructure["recentCategories"] is JsonArray recentCategories)
            {
                var target = Path.Combine(baseDirectory, "recent-categories.json");

                if (!File.Exists(target))
                {
                    var dto = new JsonObject
                    {
                        ["items"] = recentCategories.DeepClone(),
                    };

                    AtomicFile.Save(target, dto.ToJsonString(JsonStoreOptions.Default), logger);
                    logger?.LogInformation("Миграция настроек: вынесен infrastructure.recentCategories в recent-categories.json");
                }

                infrastructure.Remove("recentCategories");
                changed = true;
            }

            // obs-chat.json
            if (twitch["obsChat"] is JsonObject obsChat)
            {
                var target = Path.Combine(baseDirectory, "obs-chat.json");

                if (!File.Exists(target))
                {
                    AtomicFile.Save(target, obsChat.DeepClone()!.ToJsonString(JsonStoreOptions.Default), logger);
                    logger?.LogInformation("Миграция настроек: вынесен obsChat в obs-chat.json");
                }

                twitch.Remove("obsChat");
                changed = true;
            }
        }

        // dashboard-layout.json
        if (root["ui"] is JsonObject ui
            && (ui.ContainsKey("dashboard") || ui.ContainsKey("mainWindow")))
        {
            var target = Path.Combine(baseDirectory, "dashboard-layout.json");

            if (!File.Exists(target))
            {
                var dto = new JsonObject
                {
                    ["dashboard"] = ui["dashboard"]?.DeepClone(),
                    ["mainWindow"] = ui["mainWindow"]?.DeepClone(),
                };

                AtomicFile.Save(target, dto.ToJsonString(JsonStoreOptions.Default), logger);
                logger?.LogInformation("Миграция настроек: вынесены ui.dashboard/ui.mainWindow в dashboard-layout.json");
            }

            root.Remove("ui");
            changed = true;
        }

        return changed;
    }

    private static bool MigrateBotAccount(JsonObject twitch, ILogger? logger)
    {
        var changed = false;

        var botAccount = EnsureObject(twitch, "botAccount");

        changed |= MoveStringIfMissing(twitch, "botUsername", botAccount, "login", logger, "botUsername → botAccount.login");
        changed |= RemoveIfPresent(twitch, "accessToken", logger, "twitch.accessToken удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "refreshToken", logger, "twitch.refreshToken удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "storedScopes", logger, "twitch.storedScopes удалён (требуется новый набор прав)");
        changed |= RemoveIfPresent(twitch, "scopes", logger, "twitch.scopes удалён (требуется новый набор прав)");

        if (botAccount.Count == 0 && twitch["botAccount"] is JsonObject existing && ReferenceEquals(existing, botAccount))
        {
            twitch.Remove("botAccount");
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
