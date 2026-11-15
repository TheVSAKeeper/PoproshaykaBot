using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public sealed class BroadcastAggregatorTests
{
    private InMemoryEventBus _eventBus = null!;
    private ManualTimeProvider _timeProvider = null!;
    private FakeTargetChannelProvider _targetChannel = null!;
    private BroadcastHistoryStore _historyStore = null!;
    private ActiveBroadcastStore _activeStore = null!;
    private BroadcastDraftCoordinator _draftCoordinator = null!;
    private BroadcastAggregator _aggregator = null!;
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PoproshaykaBot-BroadcastAggregator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _timeProvider = new();
        _targetChannel = new("twitch-chan");
        _historyStore = new(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));
        _activeStore = new(
            _historyStore,
            NullLogger<ActiveBroadcastStore>.Instance,
            Path.Combine(_directory, "active_broadcast.json"),
            Path.Combine(_directory, "active_session.json"));
        _draftCoordinator = new(
            _historyStore,
            _activeStore,
            _targetChannel,
            _timeProvider,
            NullLogger<BroadcastDraftCoordinator>.Instance);
        _aggregator = new(
            _eventBus,
            _draftCoordinator,
            _targetChannel,
            _timeProvider,
            NullLogger<BroadcastAggregator>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _aggregator.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public async Task SinglePlatformOnlineAndOffline_EmitsStartedThenEndedImmediatelyWithSameBroadcastId()
    {
        var events = new List<EventBase>();
        using var sub1 = _eventBus.Subscribe<BroadcastStarted>(e => events.Add(e));
        using var sub2 = _eventBus.Subscribe<BroadcastEnded>(e => events.Add(e));

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", new StreamInfo
        {
            Id = "tw-1",
            StartedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Title = "Live",
        }));

        Assert.That(_aggregator.IsAnyLive, Is.True);
        Assert.That(_aggregator.IsPlatformLive(StreamingPlatform.Twitch), Is.True);
        Assert.That(_aggregator.CurrentBroadcastId, Is.Not.Null);

        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan"));
        await WaitUntilAsync(() => !_aggregator.IsAnyLive && events.Count == 2);

        Assert.That(_aggregator.IsAnyLive, Is.False);
        Assert.That(_aggregator.CurrentBroadcastId, Is.Null);
        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events[0], Is.TypeOf<BroadcastStarted>());
        Assert.That(events[1], Is.TypeOf<BroadcastEnded>());

        var started = (BroadcastStarted)events[0];
        var ended = (BroadcastEnded)events[1];
        Assert.That(ended.BroadcastId, Is.EqualTo(started.BroadcastId));
        Assert.That(ended.LastPlatform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(ended.LastChannel, Is.EqualTo("twitch-chan"));
    }

    [Test]
    public async Task MultiPlatformOverlap_EmitsJoinedAndLeftBeforeLastPlatformEndsBroadcast()
    {
        var events = new List<EventBase>();
        using var sub1 = _eventBus.Subscribe<BroadcastStarted>(e => events.Add(e));
        using var sub2 = _eventBus.Subscribe<BroadcastPlatformJoined>(e => events.Add(e));
        using var sub3 = _eventBus.Subscribe<BroadcastPlatformLeft>(e => events.Add(e));
        using var sub4 = _eventBus.Subscribe<BroadcastEnded>(e => events.Add(e));

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null));
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "yt-chan", null));
        _timeProvider.Advance(TimeSpan.FromMinutes(20));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan"));
        await WaitUntilAsync(() => !_aggregator.IsPlatformLive(StreamingPlatform.Twitch));

        Assert.That(_aggregator.IsAnyLive, Is.True);
        Assert.That(_aggregator.IsPlatformLive(StreamingPlatform.Twitch), Is.False);
        Assert.That(_aggregator.IsPlatformLive(StreamingPlatform.YouTube), Is.True);

        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "yt-chan"));
        await WaitUntilAsync(() => !_aggregator.IsAnyLive && events.Count == 4);

        Assert.That(_aggregator.IsAnyLive, Is.False);
        Assert.That(events.Select(e => e.GetType().Name), Is.EqualTo(new[]
        {
            nameof(BroadcastStarted),
            nameof(BroadcastPlatformJoined),
            nameof(BroadcastPlatformLeft),
            nameof(BroadcastEnded),
        }));

        var broadcastId = ((BroadcastStarted)events[0]).BroadcastId;
        Assert.That(((BroadcastPlatformJoined)events[1]).BroadcastId, Is.EqualTo(broadcastId));
        Assert.That(((BroadcastPlatformLeft)events[2]).BroadcastId, Is.EqualTo(broadcastId));
        Assert.That(((BroadcastEnded)events[3]).BroadcastId, Is.EqualTo(broadcastId));
        Assert.That(((BroadcastEnded)events[3]).LastPlatform, Is.EqualTo(StreamingPlatform.YouTube));
    }

    [Test]
    public async Task OfflineStopContinuation_RunsAfterBroadcastEnded()
    {
        var order = new List<string>();
        using var subEnded = _eventBus.Subscribe<BroadcastEnded>(_ => order.Add("BroadcastEnded"));
        using var subOffline = _eventBus.Subscribe<StreamWentOffline>((e, _) =>
        {
            _aggregator.ScheduleOfflineStop(e.Platform, _ =>
            {
                order.Add($"Stop:{e.Platform}");
                return Task.CompletedTask;
            });
            return Task.CompletedTask;
        });

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan"));
        await WaitUntilAsync(() => order.Count == 2);

        Assert.That(order, Is.EqualTo(new[] { "BroadcastEnded", "Stop:Twitch" }));
    }

    [Test]
    public async Task RapidReOnlineDuringOfflinePublish_CancelsBroadcastEndAndScheduledStop()
    {
        var endedCount = 0;
        var stopCount = 0;
        using var subEnded = _eventBus.Subscribe<BroadcastEnded>(_ => endedCount++);
        using var subOffline = _eventBus.Subscribe<StreamWentOffline>(async (e, ct) =>
        {
            _aggregator.ScheduleOfflineStop(e.Platform, _ =>
            {
                stopCount++;
                return Task.CompletedTask;
            });
            await _eventBus.PublishAsync(new StreamWentOnline(e.Platform, e.Channel, null), ct);
        });

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan"));
        await Task.Delay(50);

        Assert.That(endedCount, Is.EqualTo(0));
        Assert.That(stopCount, Is.EqualTo(0));
        Assert.That(_aggregator.IsAnyLive, Is.True);
        Assert.That(_aggregator.IsPlatformLive(StreamingPlatform.Twitch), Is.True);
    }

    [Test]
    public async Task CatchUpOffline_ClosesLiveBroadcastAndAllowsNextStream()
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subEnded = _eventBus.Subscribe<BroadcastEnded>(_ => ended.SetResult());

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", null));
        var oldId = _aggregator.CurrentBroadcastId;
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "twitch-chan", IsCatchUp: true));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(_aggregator.IsAnyLive, Is.False);
        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", new() { Id = "next" }));
        Assert.That(_aggregator.CurrentBroadcastId, Is.Not.EqualTo(oldId));
    }

    [Test]
    public async Task ConcurrentPlatforms_JoinedPublicationWaitsForStartedHandlers()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        using var startSub = _eventBus.Subscribe<BroadcastStarted>(async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            order.Add("started");
        });
        using var joinedSub = _eventBus.Subscribe<BroadcastPlatformJoined>(_ => order.Add("joined"));
        var first = _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "tw", null));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second;
        try
        {
            second = _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, "yt", null));
            Assert.That(order, Is.Empty);
            Assert.That(second.IsCompleted, Is.False);
        }
        finally
        {
            release.TrySetResult();
        }

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(order, Is.EqualTo(new[] { "started", "joined" }));
    }

    [Test]
    public async Task StartWithMatchingActiveDraft_ReusesDraftBroadcastId()
    {
        var draftId = Guid.NewGuid();
        var start = _timeProvider.GetUtcNow();
        _activeStore.TrySave(new ActiveBroadcast
        {
            BroadcastId = draftId,
            StartedAt = start,
            UpdatedAt = start.AddMinutes(20),
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "twitch-chan",
                    StreamId = "stream-1",
                    StartedAt = start,
                    UpdatedAt = start.AddMinutes(20),
                },
            ],
        });

        BroadcastStarted? started = null;
        using var sub = _eventBus.Subscribe<BroadcastStarted>(e => started = e);

        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "twitch-chan", new StreamInfo
        {
            Id = "stream-1",
            StartedAt = start.UtcDateTime,
        }, IsCatchUp: true));

        Assert.That(started, Is.Not.Null);
        Assert.That(started!.BroadcastId, Is.EqualTo(draftId));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 40; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("Timed out waiting for async condition");
    }
}
