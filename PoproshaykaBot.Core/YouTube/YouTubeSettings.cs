namespace PoproshaykaBot.Core.YouTube;

public sealed class YouTubeSettings
{
    public const int DefaultDailyQuotaBudget = 10_000;

    public bool Enabled { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public int DailyQuotaBudget { get; set; } = DefaultDailyQuotaBudget;
}
