using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Auth;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public static class YouTubeBroadcastPrivacyStatus
{
    public const string Unchanged = "";
    public const string Public = "public";
    public const string Unlisted = "unlisted";
    public const string Private = "private";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (string.Equals(trimmed, Public, StringComparison.OrdinalIgnoreCase))
        {
            return Public;
        }

        if (string.Equals(trimmed, Unlisted, StringComparison.OrdinalIgnoreCase))
        {
            return Unlisted;
        }

        if (string.Equals(trimmed, Private, StringComparison.OrdinalIgnoreCase))
        {
            return Private;
        }

        return null;
    }

    public static string GetDisplayName(string? value)
    {
        return Normalize(value) switch
        {
            Public => "Открытый",
            Unlisted => "По ссылке",
            Private => "Ограниченный",
            _ => "Не менять",
        };
    }
}

public sealed class BroadcastProfileYouTubeMetadata
{
    public string? TitleOverride { get; set; }

    public string? Description { get; set; }

    public string? PrivacyStatus { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(TitleOverride)
        && string.IsNullOrWhiteSpace(Description)
        && string.IsNullOrWhiteSpace(PrivacyStatus);

    public BroadcastProfileYouTubeMetadata Normalize()
    {
        return new()
        {
            TitleOverride = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            PrivacyStatus = YouTubeBroadcastPrivacyStatus.Normalize(PrivacyStatus),
        };
    }
}

public static class YouTubeBroadcastWriteAccess
{
    public const string FullYouTubeScope = "https://www.googleapis.com/auth/youtube";

    public static bool HasUsableCredentials(YouTubeAccountSettings? account)
    {
        return account is not null
            && (!string.IsNullOrWhiteSpace(account.AccessToken)
                || !string.IsNullOrWhiteSpace(account.RefreshToken));
    }

    public static bool HasWriteScope(YouTubeAccountSettings? account)
    {
        if (account is null || account.StoredScopes.Length == 0)
        {
            return false;
        }

        return account.StoredScopes.Any(static scope =>
            string.Equals(scope, YouTubeScopes.ForceSsl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scope, FullYouTubeScope, StringComparison.OrdinalIgnoreCase));
    }

    public static YouTubeAccountSettings? ResolveBroadcasterAccount(
        ChannelCatalogStore? catalogStore,
        YouTubeAccountsStore? accountsStore,
        out string? contextId,
        out ChannelCatalogEntry? activeChannel)
    {
        activeChannel = catalogStore?.GetActiveChannel(StreamingPlatform.YouTube);
        contextId = activeChannel?.BroadcasterContextId;

        if (accountsStore is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contextId))
        {
            if (accountsStore.TryLoadContext(PlatformAuthRole.Broadcaster, contextId, out var namedAccount))
            {
                return namedAccount;
            }

            return null;
        }

        return accountsStore.LoadBroadcaster();
    }

    public static YouTubeAccountSettings? ResolveWriteAccount(
        ChannelCatalogStore? catalogStore,
        YouTubeAccountsStore? accountsStore,
        out PlatformAuthRole role,
        out string? contextId,
        out ChannelCatalogEntry? activeChannel)
    {
        var broadcaster = ResolveBroadcasterAccount(catalogStore, accountsStore, out var broadcasterContextId, out activeChannel);
        if (HasUsableCredentials(broadcaster) && HasWriteScope(broadcaster))
        {
            role = PlatformAuthRole.Broadcaster;
            contextId = broadcasterContextId;
            return broadcaster;
        }

        if (accountsStore is not null)
        {
            var botContextId = activeChannel?.BotContextId;
            YouTubeAccountSettings? botAccount;
            if (!string.IsNullOrWhiteSpace(botContextId))
            {
                accountsStore.TryLoadContext(PlatformAuthRole.Bot, botContextId, out botAccount);
            }
            else
            {
                botAccount = accountsStore.LoadBot();
            }

            var targetChannelId = !string.IsNullOrWhiteSpace(activeChannel?.ChannelId)
                ? activeChannel.ChannelId.Trim()
                : broadcaster?.ChannelId?.Trim();

            if (HasUsableCredentials(botAccount)
                && HasWriteScope(botAccount)
                && !string.IsNullOrWhiteSpace(targetChannelId)
                && string.Equals(botAccount!.ChannelId?.Trim(), targetChannelId, StringComparison.Ordinal))
            {
                role = PlatformAuthRole.Bot;
                contextId = botContextId;
                return botAccount;
            }
        }

        role = PlatformAuthRole.Broadcaster;
        contextId = broadcasterContextId;
        return broadcaster;
    }

    public static bool CanUpdateBroadcastMetadata(
        ChannelCatalogStore? catalogStore,
        YouTubeAccountsStore? accountsStore)
    {
        var account = ResolveWriteAccount(catalogStore, accountsStore, out _, out _, out _);
        return HasUsableCredentials(account) && HasWriteScope(account);
    }
}
