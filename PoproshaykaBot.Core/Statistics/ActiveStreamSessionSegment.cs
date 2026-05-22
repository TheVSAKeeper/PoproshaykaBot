namespace PoproshaykaBot.Core.Statistics;

public sealed class ActiveStreamSessionSegment
{
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? Title { get; set; }

    public string? Game { get; set; }

    public long MessageCount { get; set; }

    public int PeakViewers { get; set; }

    public long ViewerSamplesSum { get; set; }

    public int ViewerSamplesCount { get; set; }
}
