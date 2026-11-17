using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class ActiveBroadcast
{
    public Guid BroadcastId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset? NextSampleTickUtc { get; set; }

    public List<BroadcastViewerSample> ViewerSeries { get; set; } = [];

    public List<ActiveBroadcastPlatform> Platforms { get; set; } = [];
}

public sealed class ActiveBroadcastPlatform
{
    public StreamingPlatform Platform { get; set; }

    public string Channel { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelId { get; set; }

    public string? StreamId { get; set; }

    [JsonIgnore]
    public ChannelKey? ChannelKey => Platforms.ChannelKey.FromId(Platform, ChannelId);

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? Title { get; set; }

    public string? Game { get; set; }

    public int PeakViewers { get; set; }

    public long ViewerSamplesSum { get; set; }

    public int ViewerSamplesCount { get; set; }

    public int? LastViewerCount { get; set; }

    public DateTimeOffset? LastViewerSampleAt { get; set; }

    public List<ActiveStreamSessionChatter> Chatters { get; set; } = [];

    public List<ActiveStreamSessionSegment> Segments { get; set; } = [];

    public List<ActiveStreamSessionInterval>? TrackedIntervals { get; set; }
}
