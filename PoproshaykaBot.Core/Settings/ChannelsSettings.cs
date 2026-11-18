using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Settings;

public enum ChannelSwitchMode
{
    Manual = 0,
    PreselectedNext = 1,
    AutoDiscover = 2,
}

public sealed class ChannelsSettings
{
    public List<ChannelCatalogEntry> Channels { get; set; } = [];
    public List<ChannelGroupDefinition> Groups { get; set; } = [];
    public List<PlatformChannelRoutingSettings> Platforms { get; set; } = [];
}

public sealed class ChannelCatalogEntry
{
    public StreamingPlatform Platform { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string ChannelUrl { get; set; } = string.Empty;
    public List<string> KnownAliases { get; set; } = [];
    public bool IsConfigured { get; set; }
    public bool MonitoringEnabled { get; set; }
    public string? BroadcasterAccountId { get; set; }
    public string? BroadcasterContextId { get; set; }
    public string? BroadcasterVerifiedSubjectId { get; set; }
    public string? BotAccountId { get; set; }
    public string? BotContextId { get; set; }
    public string? BotVerifiedSubjectId { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public ChannelKey Key => new(Platform, ChannelId);

    public ChannelAccountAssignment? GetAccountAssignment(PlatformAuthRole role)
    {
        var (accountId, contextId, verifiedId) = role switch
        {
            PlatformAuthRole.Broadcaster => (BroadcasterAccountId, BroadcasterContextId, BroadcasterVerifiedSubjectId),
            PlatformAuthRole.Bot => (BotAccountId, BotContextId, BotVerifiedSubjectId),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };

        var id = string.IsNullOrWhiteSpace(accountId) ? verifiedId?.Trim() : accountId.Trim();
        return string.IsNullOrWhiteSpace(id)
            ? null
            : new(Key, role, new(Platform, id), AuthContextIds.Normalize(contextId),
                string.Equals(id, verifiedId?.Trim(), StringComparison.Ordinal));
    }
}

public sealed class PlatformChannelRoutingSettings
{
    public StreamingPlatform Platform { get; set; }
    public string? ActiveChannelId { get; set; }
    public ChannelSwitchMode SwitchMode { get; set; } = ChannelSwitchMode.Manual;
    public string? PreselectedNextChannelId { get; set; }
}

public sealed class ChannelGroupMember
{
    public StreamingPlatform Platform { get; set; }
    public string ChannelId { get; set; } = string.Empty;

    [JsonIgnore]
    public ChannelKey Key => new(Platform, ChannelId);
}

public sealed class ChannelGroupDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<ChannelGroupMember> Channels { get; set; } = [];
}
