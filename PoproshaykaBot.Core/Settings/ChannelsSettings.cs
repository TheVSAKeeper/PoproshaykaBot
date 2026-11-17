using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Settings;

public sealed class ChannelsSettings
{
    public List<ChannelCatalogEntry> Channels { get; set; } = [];
    public List<ChannelGroupDefinition> Groups { get; set; } = [];
}

public sealed class ChannelCatalogEntry
{
    public StreamingPlatform Platform { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> KnownAliases { get; set; } = [];

    [JsonIgnore]
    public ChannelKey Key => new(Platform, ChannelId);
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
