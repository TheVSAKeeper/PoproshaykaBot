using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class BroadcastHistoryMergeTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 17, 6, 12, 53, TimeSpan.Zero);

    private static BroadcastRecord Record(
        DateTimeOffset startedAt,
        TimeSpan duration,
        string channel = "bobito217",
        StreamingPlatform platform = StreamingPlatform.Twitch,
        long messageCount = 0,
        int peakViewers = 0,
        int averageViewers = 0,
        string? game = null,
        string? title = null,
        bool isHidden = false,
        List<BroadcastViewerSample>? viewerSeries = null,
        List<StreamSessionChatter>? chatters = null,
        List<StreamSessionInterval>? trackedIntervals = null)
    {
        var endedAt = startedAt + duration;
        var platformRecord = new BroadcastPlatformRecord
        {
            Platform = platform,
            Channel = channel,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Title = title,
            Game = game,
            MessageCount = messageCount,
            ChatterCount = chatters?.Count ?? 0,
            PeakViewers = peakViewers,
            AverageViewers = averageViewers,
            Chatters = chatters ?? [],
            TrackedIntervals = trackedIntervals,
        };
        platformRecord.EnsureSegments();

        var record = new BroadcastRecord
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            IsHidden = isHidden,
            PeakViewers = peakViewers,
            AverageViewers = averageViewers,
            ViewerSeries = viewerSeries,
            Platforms = [platformRecord],
        };
        record.EnsureSegments();
        return record;
    }

    private static IEnumerable<TestCaseData> JoinCases()
    {
        yield return new TestCaseData(TimeSpan.FromHours(2), TimeSpan.FromMinutes(122), false)
            .SetName("Допуск начал не прибавляется ко времени конца");
        yield return new TestCaseData(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), true)
            .SetName("Граница допуска начал включена");
        yield return new TestCaseData(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1), false)
            .SetName("За границей допуска начал склейки нет");
        yield return new TestCaseData(TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(-4), true)
            .SetName("Пересекающиеся интервалы склеиваются");

        yield return new TestCaseData(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(4), true)
            .SetName("Расхождение стартов в пределах допуска склеивается без пересечения");

        yield return new TestCaseData(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(6), false)
            .SetName("Разрыв больше допуска не склеивается");

        yield return new TestCaseData(TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(100), false)
            .SetName("Соседняя запись через час после конца не склеивается");
    }

    [TestCaseSource(nameof(JoinCases))]
    public void Merge_DecidesByOverlapOrStartTolerance(TimeSpan firstDuration, TimeSpan secondOffset, bool joins)
    {
        var first = Record(Start, firstDuration);
        var second = Record(Start + secondOffset, TimeSpan.FromHours(1));

        var result = BroadcastHistoryMerge.Merge([first, second]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Broadcasts, Has.Count.EqualTo(joins ? 1 : 2));
            Assert.That(result.MergedPairs, Is.EqualTo(joins ? 1 : 0));
        }
    }

    [Test]
    public void Merge_OverlappingSameChannel_SumsMessagesAndCombinesViewersAndSegments()
    {
        var first = Record(
            Start,
            TimeSpan.FromMinutes(47),
            messageCount: 120,
            peakViewers: 31,
            averageViewers: 20,
            game: "Minecraft",
            title: "эфир",
            viewerSeries: [new(Start, 20), new(Start.AddMinutes(30), 31)]);
        var second = Record(
            Start.AddMinutes(30),
            TimeSpan.FromMinutes(30),
            messageCount: 34,
            peakViewers: 44,
            averageViewers: 40,
            game: "Just Chatting",
            viewerSeries: [new(Start.AddMinutes(35), 44)]);

        var result = BroadcastHistoryMerge.Merge([first, second]);
        var merged = result.Broadcasts.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.MergedPairs, Is.EqualTo(1));
            Assert.That(merged.Id, Is.EqualTo(first.Id));
            Assert.That(merged.Channel, Is.EqualTo("bobito217"));
            Assert.That(merged.MessageCount, Is.EqualTo(154));
            Assert.That(merged.PeakViewers, Is.EqualTo(44));
            Assert.That(merged.Title, Is.EqualTo("эфир"));
            Assert.That(merged.PrimaryPlatform!.Segments, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Merge_DifferentPlatformsOrChannels_DoNotMergeWhenDisconnected()
    {
        var twitch = Record(Start, TimeSpan.FromHours(1), channel: "tw", platform: StreamingPlatform.Twitch);
        var youtube = Record(Start.AddMinutes(1), TimeSpan.FromHours(1), channel: "yt", platform: StreamingPlatform.YouTube);

        var result = BroadcastHistoryMerge.Merge([twitch, youtube]);

        Assert.That(result.MergedPairs, Is.EqualTo(0));
        Assert.That(result.Broadcasts, Has.Count.EqualTo(2));
    }

    [TestCase(10, 10)]
    [TestCase(30, 30)]
    public void Merge_ContainedOrPartiallyOverlappingSegmentPreservesCountersAndTimeline(int offsetMinutes, int durationMinutes)
    {
        var first = Record(Start, TimeSpan.FromMinutes(40), messageCount: 10, averageViewers: 10, game: "A");
        var second = Record(Start.AddMinutes(offsetMinutes), TimeSpan.FromMinutes(durationMinutes), messageCount: 20, averageViewers: 30, game: "B");
        var merged = BroadcastHistoryMerge.Merge([first, second]).Broadcasts.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(merged.MessageCount, Is.EqualTo(30));
            Assert.That(merged.Segments.Sum(segment => segment.MessageCount), Is.EqualTo(30));
            Assert.That(merged.Segments.Sum(segment => segment.Duration.Ticks), Is.EqualTo(merged.Duration.Ticks));
            Assert.That(merged.AverageViewers, Is.EqualTo((int)Math.Round((40d * 10 + durationMinutes * 30) / (40 + durationMinutes))));
        }
    }

    [Test]
    public void Merge_ThreeOverlappingFragmentsPreserveAllIdsAndCounters()
    {
        var first = Record(Start, TimeSpan.FromMinutes(40), messageCount: 10);
        var second = Record(Start.AddMinutes(30), TimeSpan.FromMinutes(40), messageCount: 20);
        var third = Record(Start.AddMinutes(60), TimeSpan.FromMinutes(40), messageCount: 30);
        var merged = BroadcastHistoryMerge.Merge([first, second, third]).Broadcasts.Single();
        Assert.That(merged.Duration, Is.EqualTo(TimeSpan.FromMinutes(100)));
        Assert.That(merged.MessageCount, Is.EqualTo(60));
        Assert.That(merged.MergedBroadcastIds, Is.EquivalentTo(new[] { second.Id, third.Id }));
        Assert.That(merged.Segments.Sum(segment => segment.Duration.Ticks), Is.EqualTo(merged.Duration.Ticks));
    }

    [Test]
    public void Merge_HiddenRecord_IsNotMergedWithNeighbors()
    {
        var hidden = Record(Start, TimeSpan.FromHours(1), messageCount: 10, isHidden: true);
        var visible = Record(Start.AddMinutes(2), TimeSpan.FromHours(1), messageCount: 20, isHidden: false);

        var result = BroadcastHistoryMerge.Merge([hidden, visible]);

        Assert.That(result.MergedPairs, Is.EqualTo(0));
        Assert.That(result.Broadcasts, Has.Count.EqualTo(2));
    }

    [Test]
    public void Merge_CombinesChattersWithinPlatformAndPreservesTrackedIntervalsNullContract()
    {
        var first = Record(
            Start,
            TimeSpan.FromHours(1),
            chatters: [new() { UserId = "u1", DisplayName = "Alice", MessageCount = 5 }],
            trackedIntervals: [new() { StartedAt = Start, EndedAt = Start.AddMinutes(30) }]);
        var second = Record(
            Start.AddMinutes(2),
            TimeSpan.FromHours(1),
            chatters: [new() { UserId = "u1", DisplayName = "Alice", MessageCount = 3 }, new() { UserId = "u2", DisplayName = "Bob", MessageCount = 2 }],
            trackedIntervals: null);

        var merged = BroadcastHistoryMerge.Merge([first, second]).Broadcasts.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(merged.ChatterCount, Is.EqualTo(2));
            Assert.That(merged.EnumerateChatters().Single(c => c.Key.UserId == "u1").MessageCount, Is.EqualTo(8));
            Assert.That(merged.PrimaryPlatform!.TrackedIntervals, Is.Null);
        }
    }
}
