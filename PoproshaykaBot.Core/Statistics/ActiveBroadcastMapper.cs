namespace PoproshaykaBot.Core.Statistics;

internal static class ActiveBroadcastMapper
{
    public static BroadcastRecord ToBroadcastRecord(ActiveBroadcast draft, DateTimeOffset? fallbackEndedAt = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rawEnd = draft.EndedAt ?? fallbackEndedAt ?? draft.UpdatedAt;
        var startedAt = draft.StartedAt;
        var endedAt = rawEnd < startedAt ? startedAt : rawEnd;

        var platformRecords = new List<BroadcastPlatformRecord>(draft.Platforms.Count);

        foreach (var platformDraft in draft.Platforms)
        {
            platformRecords.Add(ToPlatformRecord(platformDraft, endedAt));
        }

        if (platformRecords.Count > 0)
        {
            var minStart = platformRecords.Min(static p => p.StartedAt);
            if (startedAt == default || minStart < startedAt)
            {
                startedAt = minStart;
            }

            var maxEnd = platformRecords.Max(static p => p.EndedAt);
            if (maxEnd > endedAt)
            {
                endedAt = maxEnd;
            }
        }

        List<BroadcastViewerSample>? series = draft.ViewerSeries.Count > 0
            ? NormalizeSeries(draft.ViewerSeries)
            : null;

        var (peak, average) = ComputeViewerMetrics(series, platformRecords, startedAt);

        var record = new BroadcastRecord
        {
            Id = draft.BroadcastId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            IsHidden = false,
            PeakViewers = peak,
            AverageViewers = average,
            ViewerSeries = series,
            Platforms = platformRecords,
        };

        record.EnsureSegments();
        return record;
    }

    public static (int PeakViewers, int AverageViewers) ComputeViewerMetrics(
        IReadOnlyList<BroadcastViewerSample>? series,
        IReadOnlyList<BroadcastPlatformRecord> platforms,
        DateTimeOffset startedAt)
    {
        if (series is { Count: > 0 })
        {
            var seriesPeak = series.Max(static s => s.Viewers);
            var singlePlatformPeak = platforms.Count == 1 ? platforms[0].PeakViewers : 0;
            var peak = Math.Max(seriesPeak, singlePlatformPeak);
            var average = ComputeWeightedSeriesAverage(series, startedAt);
            return (peak, average);
        }

        if (platforms.Count == 1)
        {
            return (platforms[0].PeakViewers, platforms[0].AverageViewers);
        }

        return (0, 0);
    }

    public static List<BroadcastViewerSample> NormalizeSeries(IEnumerable<BroadcastViewerSample> samples)
    {
        return
        [
            .. samples
                .GroupBy(static s => s.At)
                .OrderBy(static group => group.Key)
                .Select(static group => new BroadcastViewerSample(group.Key, group.Max(static s => s.Viewers))),
        ];
    }

    public static int ComputeWeightedSeriesAverage(IReadOnlyList<BroadcastViewerSample> series, DateTimeOffset startedAt)
    {
        if (series.Count == 0)
        {
            return 0;
        }

        if (series.Count == 1)
        {
            return series[0].Viewers;
        }

        double weighted = 0;
        double totalTicks = 0;
        var previousAt = startedAt != default && startedAt < series[0].At ? startedAt : series[0].At;

        foreach (var sample in series)
        {
            var ticks = sample.At > previousAt ? (double)(sample.At - previousAt).Ticks : 0;

            if (ticks > 0)
            {
                weighted += sample.Viewers * ticks;
                totalTicks += ticks;
            }

            previousAt = sample.At;
        }

        if (totalTicks > 0)
        {
            return (int)Math.Round(weighted / totalTicks, MidpointRounding.AwayFromZero);
        }

        return (int)Math.Round(series.Average(static s => s.Viewers), MidpointRounding.AwayFromZero);
    }

    private static BroadcastPlatformRecord ToPlatformRecord(ActiveBroadcastPlatform draft, DateTimeOffset broadcastEnd)
    {
        var rawEnd = draft.EndedAt ?? broadcastEnd;
        var startedAt = draft.StartedAt;
        var endedAt = rawEnd < startedAt ? startedAt : rawEnd;

        var segments = RestoreSegments(draft, startedAt, endedAt);

        long totalMessages = 0;
        var peakViewers = draft.PeakViewers;
        long samplesSum = 0;
        var samplesCount = 0;

        foreach (var segment in segments)
        {
            totalMessages += segment.MessageCount;
            peakViewers = Math.Max(peakViewers, segment.PeakViewers);
        }

        foreach (var sourceSegment in draft.Segments)
        {
            samplesSum += sourceSegment.ViewerSamplesSum;
            samplesCount += sourceSegment.ViewerSamplesCount;
        }

        if (samplesCount == 0 && draft.ViewerSamplesCount > 0)
        {
            samplesSum = draft.ViewerSamplesSum;
            samplesCount = draft.ViewerSamplesCount;
        }

        var chatters = draft.Chatters
            .Select(static c => new StreamSessionChatter
            {
                UserId = c.UserId,
                DisplayName = string.IsNullOrEmpty(c.DisplayName) ? c.UserId : c.DisplayName,
                MessageCount = c.MessageCount,
            })
            .OrderByDescending(static c => c.MessageCount)
            .ThenBy(static c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lastAlive = draft.EndedAt ?? (draft.UpdatedAt < endedAt ? draft.UpdatedAt : endedAt);
        List<StreamSessionInterval>? tracked = draft.TrackedIntervals is null
            ? null
            : StreamSessionInterval.Normalize(
                draft.TrackedIntervals.Select(interval => new StreamSessionInterval
                {
                    StartedAt = interval.StartedAt,
                    EndedAt = interval.EndedAt ?? (lastAlive > interval.StartedAt ? lastAlive : interval.StartedAt),
                }),
                startedAt,
                endedAt);

        var lastSegment = segments.Count > 0 ? segments[^1] : null;

        var record = new BroadcastPlatformRecord
        {
            Platform = draft.Platform,
            Channel = draft.Channel,
            StreamId = draft.StreamId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Title = lastSegment?.Title ?? draft.Title,
            Game = lastSegment?.Game ?? draft.Game,
            MessageCount = totalMessages,
            ChatterCount = chatters.Count,
            PeakViewers = peakViewers,
            AverageViewers = AverageViewers(samplesSum, samplesCount),
            Chatters = chatters,
            Segments = segments,
            TrackedIntervals = tracked,
        };

        record.EnsureSegments();
        return record;
    }

    private static List<StreamSessionSegment> RestoreSegments(
        ActiveBroadcastPlatform draft,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt)
    {
        if (draft.Segments.Count == 0)
        {
            return
            [
                new()
                {
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                    Title = draft.Title,
                    Game = draft.Game,
                    MessageCount = 0,
                    PeakViewers = draft.PeakViewers,
                    AverageViewers = AverageViewers(draft.ViewerSamplesSum, draft.ViewerSamplesCount),
                },
            ];
        }

        var result = new List<StreamSessionSegment>(draft.Segments.Count);

        for (var i = 0; i < draft.Segments.Count; i++)
        {
            var source = draft.Segments[i];
            var isLast = i == draft.Segments.Count - 1;
            var segStart = source.StartedAt == default ? startedAt : source.StartedAt;
            var segEnd = source.EndedAt ?? (isLast ? endedAt : segStart);

            if (segEnd < segStart)
            {
                segEnd = segStart;
            }

            result.Add(new()
            {
                StartedAt = segStart,
                EndedAt = segEnd,
                Title = source.Title,
                Game = source.Game,
                MessageCount = source.MessageCount,
                PeakViewers = source.PeakViewers,
                AverageViewers = AverageViewers(source.ViewerSamplesSum, source.ViewerSamplesCount),
            });
        }

        return result;
    }

    private static int AverageViewers(long sum, int count)
    {
        return count > 0
            ? (int)Math.Round((double)sum / count, MidpointRounding.AwayFromZero)
            : 0;
    }
}
