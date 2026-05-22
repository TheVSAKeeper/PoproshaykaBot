using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StreamSessionSegment
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public string? Title { get; set; }
    public string? Game { get; set; }
    public long MessageCount { get; set; }
    public int PeakViewers { get; set; }
    public int AverageViewers { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
}
