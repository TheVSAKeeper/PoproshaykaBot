using PoproshaykaBot.Core.Platforms;
using System.Security.Cryptography;
using System.Text;
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
    public int ChatterCount
    {
        get
        {
            var enumerated = EnumerateChatters().Count();
            var legacySummaryOnly = Platforms
                .Where(static p => p.Chatters.Count == 0)
                .Sum(static p => p.ChatterCount);
            return enumerated + legacySummaryOnly;
        }
    }

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

    public bool HasAnyChatter(IReadOnlySet<PlatformUserKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return false;
        }

        foreach (var key in keys)
        {
            if (HasChatter(key))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<BroadcastChatterEntry> EnumerateChatters()
    {
        var byKey = new Dictionary<PlatformUserKey, (string DisplayName, long MessageCount)>();
        var nameless = new List<BroadcastChatterEntry>();

        foreach (var platform in Platforms)
        {
            foreach (var chatter in platform.Chatters)
            {
                var displayName = string.IsNullOrEmpty(chatter.DisplayName) ? chatter.UserId : chatter.DisplayName;
                if (string.IsNullOrWhiteSpace(chatter.UserId))
                {
                    nameless.Add(new(default, displayName, chatter.MessageCount));
                    continue;
                }

                var key = new PlatformUserKey(platform.Platform, chatter.UserId);
                if (byKey.TryGetValue(key, out var existing))
                {
                    byKey[key] = (
                        string.IsNullOrEmpty(displayName) ? existing.DisplayName : displayName,
                        existing.MessageCount + chatter.MessageCount);
                }
                else
                {
                    byKey[key] = (displayName, chatter.MessageCount);
                }
            }
        }

        return byKey
            .Select(static pair => new BroadcastChatterEntry(pair.Key, pair.Value.DisplayName, pair.Value.MessageCount))
            .Concat(nameless)
            .OrderByDescending(static entry => entry.MessageCount)
            .ThenBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Key.Platform);
    }

    public void EnsureProjectionSessionIds()
    {
        var twitchRecords = Platforms.Where(static p => p.Platform == StreamingPlatform.Twitch).ToList();
        for (var i = 1; i < twitchRecords.Count; i++)
        {
            var record = twitchRecords[i];
            record.ProjectionSessionId ??= DeriveProjectionSessionId(
                Id,
                record.ChannelId ?? record.Channel,
                record.StartedAt,
                i);
        }
    }

    public IReadOnlyList<StreamSessionRecord> ProjectToTwitchSessions()
    {
        var twitchRecords = Platforms.Where(static p => p.Platform == StreamingPlatform.Twitch).ToList();
        if (twitchRecords.Count == 0)
        {
            return [];
        }

        if (twitchRecords.Count == 1)
        {
            return [twitchRecords[0].ToTwitchSessionRecord(Id, IsHidden)];
        }

        EnsureProjectionSessionIds();

        var result = new List<StreamSessionRecord>(twitchRecords.Count);
        for (var i = 0; i < twitchRecords.Count; i++)
        {
            var record = twitchRecords[i];
            var sessionId = i == 0 ? Id : record.ProjectionSessionId!.Value;
            result.Add(record.ToTwitchSessionRecord(sessionId, IsHidden));
        }

        return result;
    }

    public bool TryProjectToTwitchSession(out StreamSessionRecord session)
    {
        var projected = ProjectToTwitchSessions();
        if (projected.Count == 0)
        {
            session = null!;
            return false;
        }

        session = projected[0];
        return true;
    }

    private static Guid DeriveProjectionSessionId(Guid broadcastId, string channelIdentity, DateTimeOffset startedAt, int index)
    {
        var input = $"{broadcastId:N}|{channelIdentity}|{startedAt.UtcTicks}|{index}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
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
