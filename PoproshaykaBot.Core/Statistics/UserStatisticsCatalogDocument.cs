using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsCatalogDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentMigrationVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public UserStatisticsMigrationState? Migration { get; set; }
    public List<ViewerAccountRecord> Accounts { get; set; } = [];
    public List<ChannelUserCounterRecord> ChannelCounters { get; set; } = [];
    public List<UnattributedUserCounterRecord> UnattributedCounters { get; set; } = [];
    public List<AccountLinkGroupRecord> AccountLinks { get; set; } = [];
}

public sealed class UserStatisticsMigrationState
{
    public int Version { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public List<StreamingPlatform> MigratedPlatforms { get; set; } = [];
}

public sealed class ViewerAccountRecord
{
    public StreamingPlatform Platform { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public PlatformUserKey Key => new(Platform, UserId);

    public void UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        Name = newName;
    }

    public ViewerAccountRecord Clone()
    {
        return new()
        {
            Platform = Platform,
            UserId = UserId,
            Name = Name,
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
        };
    }
}

public sealed class ChannelUserCounterRecord
{
    public StreamingPlatform Platform { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ulong MessageCount { get; set; }
    public ulong BonusPoints { get; set; }
    public ulong PenaltyPoints { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public ChannelKey ChannelKey => new(Platform, ChannelId);

    [JsonIgnore]
    public PlatformUserKey UserKey => new(Platform, UserId);

    [JsonIgnore]
    public ChannelUserKey Key => new(ChannelKey, UserKey);

    [JsonIgnore]
    public long Points => (long)MessageCount + (long)BonusPoints - (long)PenaltyPoints;

    public ChannelUserCounterRecord Clone()
    {
        return new()
        {
            Platform = Platform,
            ChannelId = ChannelId,
            UserId = UserId,
            MessageCount = MessageCount,
            BonusPoints = BonusPoints,
            PenaltyPoints = PenaltyPoints,
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
        };
    }
}

public sealed class UnattributedUserCounterRecord
{
    public StreamingPlatform Platform { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ulong MessageCount { get; set; }
    public ulong BonusPoints { get; set; }
    public ulong PenaltyPoints { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore]
    public PlatformUserKey UserKey => new(Platform, UserId);

    [JsonIgnore]
    public long Points => (long)MessageCount + (long)BonusPoints - (long)PenaltyPoints;

    public UnattributedUserCounterRecord Clone()
    {
        return new()
        {
            Platform = Platform,
            UserId = UserId,
            MessageCount = MessageCount,
            BonusPoints = BonusPoints,
            PenaltyPoints = PenaltyPoints,
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
        };
    }
}
