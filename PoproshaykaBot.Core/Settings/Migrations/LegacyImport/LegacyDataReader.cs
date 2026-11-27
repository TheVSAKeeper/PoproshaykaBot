using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

internal static class LegacyDataReader
{
    private const long MaxInspectedFileBytes = 32L * 1024 * 1024;

    public static JsonNode? TryRead(string? filePath, ILogger? logger)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);

            if (!info.Exists)
            {
                return null;
            }

            if (info.Length > MaxInspectedFileBytes)
            {
                logger?.LogWarning("Импорт данных: файл {FilePath} слишком велик для разбора ({Length} байт) – читается только его наличие",
                    filePath,
                    info.Length);

                return null;
            }

            return JsonNode.Parse(File.ReadAllText(filePath));
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Импорт данных: не удалось прочитать {FilePath} – сведения из него не показаны", filePath);
            return null;
        }
    }

    public static JsonNode? Property(JsonNode? node, string name)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return obj.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    public static string? ReadString(JsonNode? node, string name)
    {
        if (Property(node, name) is not JsonValue value || !value.TryGetValue(out string? text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static ulong ReadUInt64(JsonNode? node, string name)
    {
        if (Property(node, name) is not JsonValue value)
        {
            return 0;
        }

        return value.TryGetValue(out ulong number) ? number : 0;
    }

    public static int CountItems(JsonNode? node)
    {
        return node switch
        {
            JsonArray array => array.Count,
            JsonObject obj => obj.Count,
            _ => 0,
        };
    }

    public static bool HasFlatTwitchTokens(JsonNode? twitch)
    {
        return AccountsTokenRedactor.AuthTokenKeys.Any(key => ReadString(twitch, key) is not null);
    }

    public static (bool BotReady, bool BroadcasterReady, bool HasBroadcasterCredentials) ReadTwitchAuthorization(
        string directory, ILogger? logger)
    {
        var accounts = ReadSettings<AccountsStore.AccountsFileDto>(directory, SettingsFileNames.AccountsFileName, logger);
        var channels = ReadSettings<ChannelsSettings>(directory, SettingsFileNames.ChannelsFileName, logger);
        var channel = channels is null ? null : ChannelCatalogStore.GetActiveChannel(channels, StreamingPlatform.Twitch);

        var bot = GetRoleAccount(PlatformAuthRole.Bot, accounts, channel);
        var broadcaster = GetRoleAccount(PlatformAuthRole.Broadcaster, accounts, channel);
        var hadBroadcaster = HasCredentials(broadcaster) || HasCredentials(accounts?.BroadcasterAccount)
            || accounts?.Contexts?.Any(context => context.Role == PlatformAuthRole.Broadcaster && HasCredentials(context.Account)) == true;

        return (TwitchScopes.HasUsableAuthorization(PlatformAuthRole.Bot, bot),
            TwitchScopes.HasUsableAuthorization(PlatformAuthRole.Broadcaster, broadcaster), hadBroadcaster);
    }

    private static T? ReadSettings<T>(string directory, string fileName, ILogger? logger) where T : class
    {
        var path = LegacyDataCatalog.ResolveSettingsFile(directory, fileName);
        try
        {
            return TryRead(path, logger)?.Deserialize<T>(JsonStoreOptions.Default);
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception, "Импорт данных: сведения об авторизации из {FilePath} не прочитаны", path);
            return null;
        }
    }

    private static TwitchAccountSettings? GetRoleAccount(
        PlatformAuthRole role, AccountsStore.AccountsFileDto? accounts, ChannelCatalogEntry? channel)
    {
        var isBot = role == PlatformAuthRole.Bot;
        var contextId = AuthContextIds.Normalize(isBot ? channel?.BotContextId : channel?.BroadcasterContextId);
        var account = AuthContextIds.IsDefault(contextId)
            ? isBot ? accounts?.BotAccount : accounts?.BroadcasterAccount
            : accounts?.Contexts?.FirstOrDefault(context => context.Role == role && context.ContextId == contextId)?.Account;
        if (channel is null)
        {
            return account;
        }

        var assignment = channel.GetAccountAssignment(role);
        return account is not null && assignment is { IsVerified: true } && assignment.Account.AccountId == account.UserId
            ? account
            : null;
    }

    private static bool HasCredentials(TwitchAccountSettings? account)
    {
        return PlatformAuthRoles.HasCredentials(account?.AccessToken, account?.RefreshToken);
    }

    public static bool HasTokens(JsonNode? node)
    {
        return AccountsTokenRedactor.HasAnyToken(node, AccountsTokenRedactor.AuthTokenKeys);
    }
}
