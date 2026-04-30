using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.WinForms.Settings.Migrations;

public static class SettingsMigrator
{
    public static bool TryMigrate(JsonObject root, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var changed = false;

        if (root["twitch"] is JsonObject twitch)
        {
            changed |= MigrateBotAccount(twitch, logger);
        }

        changed |= RemoveLegacyUiFields(root, logger);

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
