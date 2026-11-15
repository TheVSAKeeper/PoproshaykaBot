using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class BroadcastPlatformRecord
{
    public StreamingPlatform Platform { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string? StreamId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DateTimeOffset>? MergedStartedAts { get; set; }

    public DateTimeOffset EndedAt { get; set; }

    public string? Title { get; set; }

    public string? Game { get; set; }

    public long MessageCount { get; set; }

    public int ChatterCount { get; set; }

    public int PeakViewers { get; set; }

    public int AverageViewers { get; set; }

    public List<StreamSessionChatter> Chatters { get; set; } = [];

    public List<StreamSessionSegment> Segments { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StreamSessionInterval>? TrackedIntervals { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;

    [JsonIgnore]
    public TimeSpan? TrackedDuration => TrackedIntervals == null
        ? null
        : StreamSessionInterval.Normalize(TrackedIntervals, StartedAt, EndedAt)
            .Aggregate(TimeSpan.Zero, (sum, interval) => sum + interval.Duration);

    public void EnsureSegments()
    {
        if (Segments.Count > 0)
        {
            return;
        }

        Segments.Add(new()
        {
            StartedAt = StartedAt,
            EndedAt = EndedAt > StartedAt ? EndedAt : StartedAt,
            Title = Title,
            Game = Game,
            MessageCount = MessageCount,
            PeakViewers = PeakViewers,
            AverageViewers = AverageViewers,
        });
    }

    public StreamSessionRecord ToTwitchSessionRecord(Guid broadcastId, bool isHidden)
    {
        var session = new StreamSessionRecord
        {
            Id = broadcastId,
            Channel = Channel,
            StartedAt = StartedAt,
            EndedAt = EndedAt,
            Title = Title,
            Game = Game,
            MessageCount = MessageCount,
            ChatterCount = Chatters.Count > 0 ? Chatters.Count : ChatterCount,
            PeakViewers = PeakViewers,
            AverageViewers = AverageViewers,
            IsHidden = isHidden,
            Chatters =
            [
                .. Chatters.Select(static chatter => new StreamSessionChatter
                {
                    UserId = chatter.UserId,
                    DisplayName = chatter.DisplayName,
                    MessageCount = chatter.MessageCount,
                }),
            ],
            Segments =
            [
                .. Segments.Select(static segment => new StreamSessionSegment
                {
                    StartedAt = segment.StartedAt,
                    EndedAt = segment.EndedAt,
                    Title = segment.Title,
                    Game = segment.Game,
                    MessageCount = segment.MessageCount,
                    PeakViewers = segment.PeakViewers,
                    AverageViewers = segment.AverageViewers,
                }),
            ],
            TrackedIntervals = TrackedIntervals?
                .Select(static interval => new StreamSessionInterval
                {
                    StartedAt = interval.StartedAt,
                    EndedAt = interval.EndedAt,
                })
                .ToList(),
        };

        session.EnsureSegments();
        return session;
    }

    public static BroadcastPlatformRecord FromTwitchSession(StreamSessionRecord session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var platform = new BroadcastPlatformRecord
        {
            Platform = StreamingPlatform.Twitch,
            Channel = session.Channel,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            Title = session.Title,
            Game = session.Game,
            MessageCount = session.MessageCount,
            ChatterCount = session.Chatters.Count > 0 ? session.Chatters.Count : session.ChatterCount,
            PeakViewers = session.PeakViewers,
            AverageViewers = session.AverageViewers,
            Chatters =
            [
                .. session.Chatters.Select(static chatter => new StreamSessionChatter
                {
                    UserId = chatter.UserId,
                    DisplayName = chatter.DisplayName,
                    MessageCount = chatter.MessageCount,
                }),
            ],
            Segments =
            [
                .. session.Segments.Select(static segment => new StreamSessionSegment
                {
                    StartedAt = segment.StartedAt,
                    EndedAt = segment.EndedAt,
                    Title = segment.Title,
                    Game = segment.Game,
                    MessageCount = segment.MessageCount,
                    PeakViewers = segment.PeakViewers,
                    AverageViewers = segment.AverageViewers,
                }),
            ],
            TrackedIntervals = session.TrackedIntervals?
                .Select(static interval => new StreamSessionInterval
                {
                    StartedAt = interval.StartedAt,
                    EndedAt = interval.EndedAt,
                })
                .ToList(),
        };

        platform.EnsureSegments();
        return platform;
    }
}
