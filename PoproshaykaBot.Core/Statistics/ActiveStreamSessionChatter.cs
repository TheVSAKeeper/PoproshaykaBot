namespace PoproshaykaBot.Core.Statistics;

public sealed class ActiveStreamSessionChatter
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public long MessageCount { get; set; }
}
