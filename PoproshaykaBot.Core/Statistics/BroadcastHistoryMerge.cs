using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public static class BroadcastHistoryMerge
{
    public static readonly TimeSpan StartTolerance = StreamSessionStatisticsHandler.StreamMatchTolerance;

    public static BroadcastHistoryMergeResult Merge(IReadOnlyList<BroadcastRecord> broadcasts)
    {
        ArgumentNullException.ThrowIfNull(broadcasts);

        var groupOf = new int[broadcasts.Count];
        Array.Fill(groupOf, -1);

        var groups = new List<List<int>>();
        var groupBounds = new List<(DateTimeOffset StartedAt, DateTimeOffset EndedAt, HashSet<(StreamingPlatform Platform, string Channel)> Keys)>();

        var orderedIndices = Enumerable.Range(0, broadcasts.Count)
            .Where(index => !broadcasts[index].IsHidden)
            .OrderBy(index => broadcasts[index].StartedAt)
            .ThenBy(index => index);

        foreach (var index in orderedIndices)
        {
            var record = broadcasts[index];
            var startedAt = record.StartedAt;
            var endedAt = record.EndedAt > startedAt ? record.EndedAt : startedAt;
            var keys = GetPlatformKeys(record);

            var matchingGroup = FindMatchingGroup(groupBounds, keys, startedAt);

            if (matchingGroup >= 0)
            {
                groups[matchingGroup].Add(index);
                groupOf[index] = matchingGroup;

                var existing = groupBounds[matchingGroup];
                var mergedEnd = endedAt > existing.EndedAt ? endedAt : existing.EndedAt;
                var mergedStart = startedAt < existing.StartedAt ? startedAt : existing.StartedAt;
                existing.Keys.UnionWith(keys);
                groupBounds[matchingGroup] = (mergedStart, mergedEnd, existing.Keys);
                continue;
            }

            groups.Add([index]);
            groupBounds.Add((startedAt, endedAt, keys));
            groupOf[index] = groups.Count - 1;
        }

        var merged = new List<BroadcastRecord>(broadcasts.Count);
        var emitted = new HashSet<int>();
        var mergedPairs = 0;

        for (var index = 0; index < broadcasts.Count; index++)
        {
            var groupIndex = groupOf[index];

            if (groupIndex < 0)
            {
                merged.Add(broadcasts[index]);
                continue;
            }

            if (!emitted.Add(groupIndex))
            {
                continue;
            }

            var group = groups[groupIndex];

            if (group.Count == 1)
            {
                merged.Add(broadcasts[group[0]]);
                continue;
            }

            mergedPairs += group.Count - 1;
            merged.Add(Combine(group.ConvertAll(member => broadcasts[member])));
        }

        return new(merged, mergedPairs);
    }

    private static int FindMatchingGroup(
        List<(DateTimeOffset StartedAt, DateTimeOffset EndedAt, HashSet<(StreamingPlatform Platform, string Channel)> Keys)> groupBounds,
        HashSet<(StreamingPlatform Platform, string Channel)> candidateKeys,
        DateTimeOffset startedAt)
    {
        for (var i = groupBounds.Count - 1; i >= 0; i--)
        {
            var group = groupBounds[i];

            if (!SharesPlatformChannel(group.Keys, candidateKeys))
            {
                continue;
            }

            if (Joins(group.StartedAt, group.EndedAt, startedAt))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool SharesPlatformChannel(
        HashSet<(StreamingPlatform Platform, string Channel)> left,
        HashSet<(StreamingPlatform Platform, string Channel)> right)
    {
        foreach (var item in right)
        {
            if (left.Contains(item))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Joins(DateTimeOffset groupStart, DateTimeOffset groupEnd, DateTimeOffset startedAt)
    {
        return startedAt <= groupEnd + StartTolerance || (startedAt - groupStart).Duration() <= StartTolerance;
    }

    private static HashSet<(StreamingPlatform Platform, string Channel)> GetPlatformKeys(BroadcastRecord record)
    {
        var set = new HashSet<(StreamingPlatform, string)>();

        foreach (var platform in record.Platforms)
        {
            set.Add((platform.Platform, platform.Channel.ToUpperInvariant()));
        }

        return set;
    }

    private static BroadcastRecord Combine(IReadOnlyList<BroadcastRecord> group)
    {
        var primary = group[0];
        var startedAt = group.Min(static r => r.StartedAt);
        var endedAt = group.Max(static r => r.EndedAt);

        if (endedAt < startedAt)
        {
            endedAt = startedAt;
        }

        var mergedPlatforms = CombinePlatforms(group);
        var mergedSeries = CombineViewerSeries(group);
        var (peakViewers, averageViewers) = CombineBroadcastViewers(group, mergedSeries, startedAt);

        var result = new BroadcastRecord
        {
            Id = primary.Id,
            StartedAt = startedAt,
            EndedAt = endedAt,
            IsHidden = false,
            PeakViewers = peakViewers,
            AverageViewers = averageViewers,
            ViewerSeries = mergedSeries,
            Platforms = mergedPlatforms,
        };

        result.EnsureSegments();
        return result;
    }

    private static List<BroadcastPlatformRecord> CombinePlatforms(IReadOnlyList<BroadcastRecord> group)
    {
        var byPlatform = new Dictionary<StreamingPlatform, List<BroadcastPlatformRecord>>();
        var order = new List<StreamingPlatform>();

        foreach (var broadcast in group)
        {
            foreach (var platformRecord in broadcast.Platforms)
            {
                if (!byPlatform.TryGetValue(platformRecord.Platform, out var list))
                {
                    list = [];
                    byPlatform[platformRecord.Platform] = list;
                    order.Add(platformRecord.Platform);
                }

                list.Add(platformRecord);
            }
        }

        var result = new List<BroadcastPlatformRecord>(order.Count);

        foreach (var platform in order)
        {
            var items = byPlatform[platform];
            result.Add(items.Count == 1 ? ClonePlatform(items[0]) : CombineSinglePlatform(platform, items));
        }

        return result;
    }

    private static BroadcastPlatformRecord CombineSinglePlatform(
        StreamingPlatform platform,
        IReadOnlyList<BroadcastPlatformRecord> group)
    {
        var first = group[0];
        var startedAt = group.Min(static r => r.StartedAt);
        var endedAt = group.Max(static r => r.EndedAt);

        if (endedAt < startedAt)
        {
            endedAt = startedAt;
        }

        var chatters = MergeChatters(group);
        var streamId = group.Select(static r => r.StreamId).FirstOrDefault(static id => !string.IsNullOrEmpty(id));

        return new()
        {
            Platform = platform,
            Channel = first.Channel,
            StreamId = streamId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Title = LongestNonEmpty(group, static r => r.Title),
            Game = LongestNonEmpty(group, static r => r.Game),
            MessageCount = group.Sum(static r => r.MessageCount),
            ChatterCount = chatters.Count > 0 ? chatters.Count : group.Max(static r => r.ChatterCount),
            PeakViewers = group.Max(static r => r.PeakViewers),
            AverageViewers = WeightedAveragePlatformViewers(group),
            Chatters = chatters,
            Segments = MergeSegments(group),
            TrackedIntervals = group.All(static r => r.TrackedIntervals != null)
                ? StreamSessionInterval.Normalize(group.SelectMany(static r => r.TrackedIntervals!), startedAt, endedAt)
                : null,
        };
    }

    private static List<BroadcastViewerSample>? CombineViewerSeries(IReadOnlyList<BroadcastRecord> group)
    {
        var points = new List<BroadcastViewerSample>();
        var anySeries = false;

        foreach (var record in group)
        {
            if (record.ViewerSeries is { Count: > 0 } series)
            {
                anySeries = true;
                points.AddRange(series);
            }
            else if (record.PeakViewers > 0 || record.AverageViewers > 0)
            {
                var start = record.StartedAt;
                var end = record.EndedAt > start ? record.EndedAt : start;
                points.Add(new(start, record.AverageViewers > 0 ? record.AverageViewers : record.PeakViewers));
                if (end > start)
                {
                    points.Add(new(end, record.AverageViewers > 0 ? record.AverageViewers : record.PeakViewers));
                }
            }
        }

        return anySeries ? ActiveBroadcastMapper.NormalizeSeries(points) : null;
    }

    private static (int PeakViewers, int AverageViewers) CombineBroadcastViewers(
        IReadOnlyList<BroadcastRecord> group,
        IReadOnlyList<BroadcastViewerSample>? mergedSeries,
        DateTimeOffset startedAt)
    {
        var groupMaxPeak = group.Max(static r => r.PeakViewers);

        if (mergedSeries is { Count: > 0 })
        {
            var seriesPeak = Math.Max(groupMaxPeak, mergedSeries.Max(static s => s.Viewers));
            var average = ActiveBroadcastMapper.ComputeWeightedSeriesAverage(mergedSeries, startedAt);
            return (seriesPeak, average);
        }

        return (groupMaxPeak, WeightedAverageBroadcastViewers(group));
    }

    private static List<StreamSessionSegment> MergeSegments(IReadOnlyList<BroadcastPlatformRecord> group)
    {
        var merged = new List<StreamSessionSegment>();

        StreamSessionSegment? tail = null;
        double weightedViewers = 0;
        double weightTicks = 0;
        var folded = false;

        var ordered = group
            .SelectMany(static record => record.Segments)
            .OrderBy(static segment => segment.StartedAt)
            .ThenBy(static segment => segment.EndedAt);

        foreach (var source in ordered)
        {
            var startedAt = source.StartedAt;
            var endedAt = source.EndedAt > startedAt ? source.EndedAt : startedAt;

            if (tail != null && endedAt <= tail.EndedAt)
            {
                tail.MessageCount += source.MessageCount;
                tail.PeakViewers = Math.Max(tail.PeakViewers, source.PeakViewers);

                weightedViewers += source.AverageViewers * (double)(endedAt - startedAt).Ticks;
                weightTicks += (endedAt - startedAt).Ticks;
                folded = true;

                continue;
            }

            CloseTail();

            if (tail != null && startedAt < tail.EndedAt)
            {
                startedAt = tail.EndedAt;
            }

            tail = new()
            {
                StartedAt = startedAt,
                EndedAt = endedAt,
                Title = source.Title,
                Game = source.Game,
                MessageCount = source.MessageCount,
                PeakViewers = source.PeakViewers,
                AverageViewers = source.AverageViewers,
            };

            weightedViewers = source.AverageViewers * (double)(endedAt - source.StartedAt).Ticks;
            weightTicks = (endedAt - source.StartedAt).Ticks;
            folded = false;

            merged.Add(tail);
        }

        CloseTail();

        return merged;

        void CloseTail()
        {
            if (tail == null || !folded || weightTicks <= 0)
            {
                return;
            }

            tail.AverageViewers = (int)Math.Round(weightedViewers / weightTicks, MidpointRounding.AwayFromZero);
        }
    }

    private static string? LongestNonEmpty(
        IReadOnlyList<BroadcastPlatformRecord> group,
        Func<BroadcastPlatformRecord, string?> selector)
    {
        return group
            .Where(record => !string.IsNullOrWhiteSpace(selector(record)))
            .OrderByDescending(static record => record.Duration)
            .Select(selector)
            .FirstOrDefault();
    }

    private static int WeightedAveragePlatformViewers(IReadOnlyList<BroadcastPlatformRecord> group)
    {
        return WeightedAverageCore(group, static r => r.Duration, static r => r.AverageViewers);
    }

    private static int WeightedAverageBroadcastViewers(IReadOnlyList<BroadcastRecord> group)
    {
        return WeightedAverageCore(group, static r => r.Duration, static r => r.AverageViewers);
    }

    private static int WeightedAverageCore<T>(
        IReadOnlyList<T> group,
        Func<T, TimeSpan> durationSelector,
        Func<T, int> averageSelector)
    {
        double weighted = 0;
        double totalTicks = 0;

        foreach (var record in group)
        {
            var ticks = (double)durationSelector(record).Ticks;

            if (ticks <= 0)
            {
                continue;
            }

            weighted += averageSelector(record) * ticks;
            totalTicks += ticks;
        }

        if (totalTicks > 0)
        {
            return (int)Math.Round(weighted / totalTicks, MidpointRounding.AwayFromZero);
        }

        var known = group
            .Select(averageSelector)
            .Where(static average => average != 0)
            .ToList();

        return known.Count > 0 ? (int)Math.Round(known.Average(), MidpointRounding.AwayFromZero) : 0;
    }

    private static List<StreamSessionChatter> MergeChatters(IReadOnlyList<BroadcastPlatformRecord> group)
    {
        var merged = new Dictionary<(bool HasUserId, string Key), StreamSessionChatter>();

        foreach (var record in group)
        {
            foreach (var chatter in record.Chatters)
            {
                var hasUserId = !string.IsNullOrEmpty(chatter.UserId);
                var key = (hasUserId, hasUserId ? chatter.UserId : chatter.DisplayName);

                if (merged.TryGetValue(key, out var existing))
                {
                    existing.MessageCount += chatter.MessageCount;

                    if (!string.IsNullOrEmpty(chatter.DisplayName))
                    {
                        existing.DisplayName = chatter.DisplayName;
                    }

                    continue;
                }

                merged[key] = new()
                {
                    UserId = chatter.UserId,
                    DisplayName = chatter.DisplayName,
                    MessageCount = chatter.MessageCount,
                };
            }
        }

        return merged.Values
            .OrderByDescending(static chatter => chatter.MessageCount)
            .ThenBy(static chatter => chatter.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BroadcastPlatformRecord ClonePlatform(BroadcastPlatformRecord source)
    {
        return new()
        {
            Platform = source.Platform,
            Channel = source.Channel,
            StreamId = source.StreamId,
            StartedAt = source.StartedAt,
            EndedAt = source.EndedAt,
            Title = source.Title,
            Game = source.Game,
            MessageCount = source.MessageCount,
            ChatterCount = source.ChatterCount,
            PeakViewers = source.PeakViewers,
            AverageViewers = source.AverageViewers,
            Chatters =
            [
                .. source.Chatters.Select(static c => new StreamSessionChatter
                {
                    UserId = c.UserId,
                    DisplayName = c.DisplayName,
                    MessageCount = c.MessageCount,
                }),
            ],
            Segments =
            [
                .. source.Segments.Select(static s => new StreamSessionSegment
                {
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    Title = s.Title,
                    Game = s.Game,
                    MessageCount = s.MessageCount,
                    PeakViewers = s.PeakViewers,
                    AverageViewers = s.AverageViewers,
                }),
            ],
            TrackedIntervals = source.TrackedIntervals?
                .Select(static interval => new StreamSessionInterval
                {
                    StartedAt = interval.StartedAt,
                    EndedAt = interval.EndedAt,
                })
                .ToList(),
        };
    }
}
