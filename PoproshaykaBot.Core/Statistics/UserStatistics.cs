using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public class UserStatistics
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ulong MessageCount { get; set; }
    public ulong PenaltyPoints { get; set; }
    public ulong BonusPoints { get; set; }

    [JsonIgnore]
    public long Points => (long)MessageCount + (long)BonusPoints - (long)PenaltyPoints;

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public static UserStatistics Create(string userId, string name)
    {
        var now = DateTime.UtcNow;

        return new()
        {
            UserId = userId,
            Name = name,
            MessageCount = 0,
            FirstSeen = now,
            LastSeen = now,
        };
    }

    public void IncrementMessageCount()
    {
        MessageCount++;
        LastSeen = DateTime.UtcNow;
    }

    public void UpdateName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || Name == newName)
        {
            return;
        }

        Name = newName;
    }

    public UserStatistics Clone()
    {
        return new()
        {
            UserId = UserId,
            Name = Name,
            MessageCount = MessageCount,
            PenaltyPoints = PenaltyPoints,
            BonusPoints = BonusPoints,
            FirstSeen = FirstSeen,
            LastSeen = LastSeen,
        };
    }

    [JsonPropertyName("bonusMessageCount")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal ulong LegacyBonusMessageCount
    {
        get => 0;
        set => BonusPoints = value;
    }

    [JsonPropertyName("shtrafMessageCount")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal ulong LegacyShtrafMessageCount
    {
        get => 0;
        set => PenaltyPoints = value;
    }
}
