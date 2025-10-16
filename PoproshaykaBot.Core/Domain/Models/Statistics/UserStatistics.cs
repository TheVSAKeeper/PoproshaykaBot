namespace PoproshaykaBot.Core.Domain.Models.Statistics;

public class UserStatistics
{
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ulong MessageCount { get; set; }

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
}
