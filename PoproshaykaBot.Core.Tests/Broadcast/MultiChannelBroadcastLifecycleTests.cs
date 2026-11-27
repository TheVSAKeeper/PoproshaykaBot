using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public sealed class MultiChannelBroadcastLifecycleTests
{
    private string _directory = null!;
    private ManualTimeProvider _time = null!;
    private InMemoryEventBus _eventBus = null!;
    private BroadcastHistoryStore _historyStore = null!;
    private ActiveBroadcastStore _activeStore = null!;
    private BroadcastDraftCoordinator _drafts = null!;
    private BroadcastAggregator _aggregator = null!;
    private StreamSessionStatisticsHandler _handler = null!;
    private List<BroadcastRecord> _completed = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-mc-life-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _time = new();
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _completed = [];
        _eventBus.Subscribe<StreamSessionCompleted>(e => _completed.Add(e.Broadcast));

        _historyStore = new(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));
        _activeStore = new(
            _historyStore,
            NullLogger<ActiveBroadcastStore>.Instance,
            Path.Combine(_directory, "active_broadcast.json"),
            Path.Combine(_directory, "active_session.json"));

        var targetChannel = new FakeTargetChannelProvider("tw_main");
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        settingsManager.Current.Returns(new AppSettings { Twitch = { Channel = "tw_main" } });

        _drafts = new(_historyStore, _activeStore, targetChannel, _time, NullLogger<BroadcastDraftCoordinator>.Instance);
        _aggregator = new(_eventBus, _drafts, targetChannel, _time, NullLogger<BroadcastAggregator>.Instance);
        _handler = new(
            Substitute.For<IBroadcastMessenger>(),
            _aggregator,
            _drafts,
            targetChannel,
            settingsManager,
            _time,
            _eventBus,
            _activeStore,
            NullLogger<StreamSessionStatisticsHandler>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _handler.DisposeAsync();
        _aggregator.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Combined_TwitchA_YouTubeA_replace_to_YouTubeB_keeps_single_broadcast_and_prevents_peak_double_count()
    {
        var endedEvents = new List<BroadcastEnded>();
        using var endedSub = _eventBus.Subscribe<BroadcastEnded>(e => endedEvents.Add(e));

        var t0 = _time.GetUtcNow();
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.Twitch,
            "tw_main",
            new() { Id = "same-id", StartedAt = t0.UtcDateTime, ViewerCount = 30 },
            ChannelId: "tw-a"));
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "Канал A",
            new() { Id = "same-id", StartedAt = t0.UtcDateTime, ViewerCount = 40 },
            ChannelId: "UC_A"));

        await _time.WaitForPendingTimersAsync(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForSamplerAsync(1);

        var t1 = new DateTimeOffset(_time.GetUtcNow().UtcDateTime, TimeSpan.Zero);
        var snapTw1 = new ChannelMonitoringSnapshot(
            new(StreamingPlatform.Twitch, "tw-a"),
            "tw_main",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Online,
            new() { Id = "same-id", StartedAt = t0.UtcDateTime, ViewerCount = 30 },
            t1,
            false);
        var snapYtA1 = new ChannelMonitoringSnapshot(
            new(StreamingPlatform.YouTube, "UC_A"),
            "Канал A",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Online,
            new() { Id = "same-id", StartedAt = t0.UtcDateTime, ViewerCount = 40 },
            t1,
            false);
        await _eventBus.PublishAsync(new ChannelStreamObserved(snapTw1, snapTw1));
        await _eventBus.PublishAsync(new ChannelStreamObserved(snapYtA1, snapYtA1));

        _time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(30);

        _time.Advance(TimeSpan.FromMinutes(8));
        await _aggregator.ReplaceActiveChannelAsync(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, "UC_A"),
            new ChannelCatalogEntry { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B" },
            new StreamInfo { Id = "same-id", StartedAt = _time.GetUtcNow().UtcDateTime, ViewerCount = 25 });

        Assert.That(endedEvents, Is.Empty);

        var tick = new DateTimeOffset(_time.GetUtcNow().UtcDateTime, TimeSpan.Zero);
        var snapTw = new ChannelMonitoringSnapshot(
            new(StreamingPlatform.Twitch, "tw-a"),
            "tw_main",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Online,
            new() { Id = "same-id", StartedAt = t0.UtcDateTime, ViewerCount = 30 },
            tick,
            false);
        var snapYtAOffline = new ChannelMonitoringSnapshot(
            new(StreamingPlatform.YouTube, "UC_A"),
            "Канал A",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Offline,
            null,
            tick,
            false);
        var snapYtBOnline = new ChannelMonitoringSnapshot(
            new(StreamingPlatform.YouTube, "UC_B"),
            "Канал B",
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            StreamStatus.Online,
            new() { Id = "same-id", StartedAt = tick.UtcDateTime, ViewerCount = 25 },
            tick,
            false);

        await _eventBus.PublishAsync(new ChannelStreamObserved(snapTw, snapTw));
        await _eventBus.PublishAsync(new ChannelStreamObserved(snapYtAOffline, snapYtAOffline));
        await _eventBus.PublishAsync(new ChannelStreamObserved(snapYtBOnline, snapYtBOnline));

        await _time.WaitForPendingTimersAsync(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        await WaitForSamplerAsync(2);

        _time.Advance(TimeSpan.FromMinutes(19));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "Канал B", ChannelId: "UC_B"));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "tw_main", ChannelId: "tw-a"));

        await WaitUntilAsync(() => _completed.Count == 1);
        var record = _completed[0];
        Assert.Multiple(() =>
        {
            Assert.That(record.Platforms, Has.Count.EqualTo(3));
            Assert.That(record.Platforms.Select(c => c.ChannelId), Is.EquivalentTo(new[] { "tw-a", "UC_A", "UC_B" }));
            Assert.That(record.PeakViewers, Is.EqualTo(70));
        });
    }

    [Test]
    public async Task Leaving_active_channel_to_offline_channel_finalizes_previous_broadcast_before_new_one_starts()
    {
        var broadcastIds = new List<Guid>();
        using var startSub = _eventBus.Subscribe<BroadcastStarted>(e => broadcastIds.Add(e.BroadcastId));

        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "Канал A",
            new() { Id = "yt-1", StartedAt = _time.GetUtcNow().UtcDateTime, ViewerCount = 15 },
            ChannelId: "UC_A"));

        _time.Advance(TimeSpan.FromMinutes(15));
        await _aggregator.LeaveActiveChannelAsync(StreamingPlatform.YouTube, new(StreamingPlatform.YouTube, "UC_A"));

        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "Канал B",
            new() { Id = "yt-2", StartedAt = _time.GetUtcNow().UtcDateTime, ViewerCount = 20 },
            ChannelId: "UC_B"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "Канал B", ChannelId: "UC_B"));

        await WaitUntilAsync(() => _completed.Count == 2);

        Assert.Multiple(() =>
        {
            Assert.That(broadcastIds, Has.Count.EqualTo(2));
            Assert.That(broadcastIds[0], Is.Not.EqualTo(broadcastIds[1]));
            Assert.That(_completed, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Returning_to_previous_channel_A_after_B_clamps_reopened_segment_start_after_B_end()
    {
        var t0 = _time.GetUtcNow();
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "Канал A",
            new() { Id = "yt-a1", StartedAt = t0.UtcDateTime },
            ChannelId: "UC_A"));

        _time.Advance(TimeSpan.FromMinutes(10));
        await _aggregator.ReplaceActiveChannelAsync(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, "UC_A"),
            new ChannelCatalogEntry { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B" },
            new StreamInfo { Id = "yt-b1", StartedAt = _time.GetUtcNow().UtcDateTime });

        _time.Advance(TimeSpan.FromMinutes(10));
        await _aggregator.ReplaceActiveChannelAsync(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, "UC_B"),
            new ChannelCatalogEntry { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A" },
            new StreamInfo { Id = "yt-a2", StartedAt = t0.UtcDateTime });

        _time.Advance(TimeSpan.FromMinutes(5));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "Канал A", ChannelId: "UC_A"));

        await WaitUntilAsync(() => _completed.Count == 1);
        var ytA = _completed[0].Platforms.Single(p => p.ChannelId == "UC_A");
        var ytB = _completed[0].Platforms.Single(p => p.ChannelId == "UC_B");
        var boundAfterB = ytB.EndedAt != default ? ytB.EndedAt : ytB.Segments[0].StartedAt;
        Assert.Multiple(() =>
        {
            Assert.That(ytA.Segments, Has.Count.EqualTo(2));
            Assert.That(ytA.Segments[1].StartedAt, Is.GreaterThanOrEqualTo(boundAfterB));
        });
    }

    private Task WaitForSamplerAsync(int expectedSamples) =>
        WaitUntilAsync(() => (_activeStore.Load()?.ViewerSeries.Count ?? 0) >= expectedSamples);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("Timed out waiting for async broadcast completion.");
    }
}
