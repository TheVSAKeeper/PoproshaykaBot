using PoproshaykaBot.Core.Platforms;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Statistics;

public sealed class BroadcastRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? MergedBroadcastIds { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset EndedAt { get; set; }

    public bool IsHidden { get; set; }

    public int PeakViewers { get; set; }

    public int AverageViewers { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BroadcastViewerSample>? ViewerSeries { get; set; }

    public List<BroadcastPlatformRecord> Platforms { get; set; } = [];

    [JsonIgnore]
    public BroadcastPlatformRecord? PrimaryPlatform =>
        Platforms.FirstOrDefault(p => p.Platform == StreamingPlatform.Twitch)
        ?? Platforms.FirstOrDefault();

    [JsonIgnore]
    public string Channel => PrimaryPlatform?.Channel ?? string.Empty;

    [JsonIgnore]
    public string? Title => PrimaryPlatform?.Title;

    [JsonIgnore]
    public string? Game => PrimaryPlatform?.Game;

    [JsonIgnore]
    public long MessageCount => Platforms.Sum(static p => p.MessageCount);

    [JsonIgnore]
    public int ChatterCount => Platforms.Sum(static p => p.Chatters.Count > 0 ? p.Chatters.Count : p.ChatterCount);

    [JsonIgnore]
    public IReadOnlyList<StreamSessionSegment> Segments => PrimaryPlatform?.Segments ?? [];

    [JsonIgnore]
    public IReadOnlyList<StreamSessionInterval>? TrackedIntervals => PrimaryPlatform?.TrackedIntervals;

    [JsonIgnore]
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;

    [JsonIgnore]
    public TimeSpan? TrackedDuration
    {
        get
        {
            if (Platforms.Count == 0)
            {
                return null;
            }

            if (Platforms.Count == 1)
            {
                return Platforms[0].TrackedDuration;
            }

            if (Platforms.Any(static p => p.TrackedIntervals == null))
            {
                return null;
            }

            return StreamSessionInterval.Normalize(
                    Platforms.SelectMany(static p => p.TrackedIntervals!),
                    StartedAt,
                    EndedAt)
                .Aggregate(TimeSpan.Zero, (sum, interval) => sum + interval.Duration);
        }
    }

    public void EnsureSegments()
    {
        foreach (var platform in Platforms)
        {
            platform.EnsureSegments();
        }
    }

    public bool HasChatter(PlatformUserKey key)
    {
        if (string.IsNullOrEmpty(key.UserId))
        {
            return false;
        }

        foreach (var platform in Platforms)
        {
            if (platform.Platform != key.Platform)
            {
                continue;
            }

            if (platform.Chatters.Exists(c => string.Equals(c.UserId, key.UserId, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<BroadcastChatterEntry> EnumerateChatters()
    {
        return Platforms
            .SelectMany(static platform => platform.Chatters.Select(chatter => new BroadcastChatterEntry(
                new(platform.Platform, chatter.UserId),
                string.IsNullOrEmpty(chatter.DisplayName) ? chatter.UserId : chatter.DisplayName,
                chatter.MessageCount)))
            .OrderByDescending(static entry => entry.MessageCount)
            .ThenBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Key.Platform);
    }

    public bool TryProjectToTwitchSession(out StreamSessionRecord session)
    {
        var twitch = Platforms.FirstOrDefault(static p => p.Platform == StreamingPlatform.Twitch);

        if (twitch is null)
        {
            session = null!;
            return false;
        }

        session = twitch.ToTwitchSessionRecord(Id, IsHidden);
        return true;
    }

    public static BroadcastRecord FromTwitchSession(StreamSessionRecord session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new()
        {
            Id = session.Id,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            IsHidden = session.IsHidden,
            PeakViewers = session.PeakViewers,
            AverageViewers = session.AverageViewers,
            ViewerSeries = null,
            Platforms = [BroadcastPlatformRecord.FromTwitchSession(session)],
        };
    }
}
