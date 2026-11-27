using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using PoproshaykaBot.Core.Users;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Broadcast;

[TestFixture]
public sealed class StreamSessionStatisticsHandlerTests
{
    private const string Channel = "test_channel";
    private const string YouTubeChannel = "yt_channel";
    private static readonly DateTime StreamStartUtc = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset StreamStart = new(StreamStartUtc);

    private IBroadcastMessenger _messenger = null!;
    private AppSettings _settings = null!;
    private SettingsManager _settingsManager = null!;
    private ManualTimeProvider _timeProvider = null!;
    private FakeTargetChannelProvider _targetChannel = null!;
    private InMemoryEventBus _eventBus = null!;
    private BroadcastHistoryStore _historyStore = null!;
    private ActiveBroadcastStore _store = null!;
    private BroadcastDraftCoordinator _draftCoordinator = null!;
    private BroadcastAggregator _aggregator = null!;
    private StreamSessionStatisticsHandler _handler = null!;
    private List<BroadcastRecord> _captured = null!;
    private string _tempDirectory = null!;
    private string _broadcastsPath = null!;
    private string _sessionsPath = null!;
    private string _activePath = null!;
    private string _legacyActivePath = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"stream-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _broadcastsPath = Path.Combine(_tempDirectory, "broadcasts.json");
        _sessionsPath = Path.Combine(_tempDirectory, "stream_sessions.json");
        _activePath = Path.Combine(_tempDirectory, "active_broadcast.json");
        _legacyActivePath = Path.Combine(_tempDirectory, "active_session.json");

        _messenger = Substitute.For<IBroadcastMessenger>();
        _settings = new()
        {
            Twitch =
            {
                Channel = Channel,
                AutoBroadcast =
                {
                    StreamStatusNotificationsEnabled = true,
                    StreamEndStatsMessage = "Эфир длился {duration}: {messages} сообщений, {chatters} участников.",
                },
            },
        };
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        _settingsManager.Current.Returns(_settings);

        _timeProvider = new();
        _targetChannel = new(Channel);
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _captured = [];
        _eventBus.Subscribe<StreamSessionCompleted>(@event => _captured.Add(@event.Broadcast));

        _historyStore = new(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
        _store = new(_historyStore, NullLogger<ActiveBroadcastStore>.Instance, _activePath, _legacyActivePath);
        _draftCoordinator = new(_historyStore, _store, _targetChannel, _timeProvider, NullLogger<BroadcastDraftCoordinator>.Instance);
        _aggregator = new(_eventBus, _draftCoordinator, _targetChannel, _timeProvider, NullLogger<BroadcastAggregator>.Instance);

        _handler = CreateHandler();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _handler.DisposeAsync();
        _aggregator.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    private StreamSessionStatisticsHandler CreateHandler()
    {
        return new(
            _messenger,
            _aggregator,
            _draftCoordinator,
            _targetChannel,
            _settingsManager,
            _timeProvider,
            _eventBus,
            _store,
            NullLogger<StreamSessionStatisticsHandler>.Instance);
    }

    private static StreamWentOnline Online(
        string? title = "Заголовок",
        string? game = "Игра",
        string streamId = "",
        int viewers = 10,
        DateTime? startedAtUtc = null,
        bool isCatchUp = false)
    {
        return new(StreamingPlatform.Twitch, Channel, new()
        {
            Id = streamId,
            StartedAt = startedAtUtc ?? StreamStartUtc,
            ViewerCount = viewers,
            Title = title ?? string.Empty,
            GameName = game ?? string.Empty,
        }, isCatchUp);
    }

    private static StreamWentOnline OnlineYouTube(int viewers = 20, string? title = "YT Stream", string? game = "Just Chatting", DateTime? startedAtUtc = null)
    {
        return new(StreamingPlatform.YouTube, YouTubeChannel, new()
        {
            StartedAt = startedAtUtc ?? StreamStartUtc,
            ViewerCount = viewers,
            Title = title ?? string.Empty,
            GameName = game ?? string.Empty,
        });
    }

    private static ChatMessageReceived Chat(
        StreamingPlatform platform,
        string channel,
        string userId,
        string displayName,
        bool isBot = false)
    {
        return new(platform, channel, Guid.NewGuid().ToString("N"), userId, displayName.ToLowerInvariant(), displayName,
            "сообщение", UserStatus.None, false, new() { Platform = platform }, isBot);
    }

    private static ChatMessageReceived TwitchChat(string userId, string displayName, bool isBot = false)
    {
        return Chat(StreamingPlatform.Twitch, Channel, userId, displayName, isBot);
    }

    private void SeedTwitchDraft(
        Guid? broadcastId,
        string? streamId,
        DateTimeOffset startedAt,
        DateTimeOffset updatedAt,
        long messageCount,
        params (string UserId, string DisplayName, long Count)[] chatters)
    {
        _store.TrySave(new ActiveBroadcast
        {
            BroadcastId = broadcastId ?? Guid.NewGuid(),
            StartedAt = startedAt,
            UpdatedAt = updatedAt,
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = Channel,
                    StreamId = streamId,
                    StartedAt = startedAt,
                    UpdatedAt = updatedAt,
                    PeakViewers = 50,
                    ViewerSamplesSum = 300,
                    ViewerSamplesCount = 10,
                    Title = "Черновой заголовок",
                    Game = "Черновая игра",
                    Segments =
                    [
                        new ActiveStreamSessionSegment
                        {
                            StartedAt = startedAt,
                            Title = "Черновой заголовок",
                            Game = "Черновая игра",
                            MessageCount = messageCount,
                            PeakViewers = 50,
                            ViewerSamplesSum = 300,
                            ViewerSamplesCount = 10,
                        },
                    ],
                    Chatters =
                    [
                        .. chatters.Select(chatter => new ActiveStreamSessionChatter
                        {
                            UserId = chatter.UserId,
                            DisplayName = chatter.DisplayName,
                            MessageCount = chatter.Count,
                        }),
                    ],
                },
            ],
        });
    }

    [Test]
    public async Task StreamWentOffline_AfterSession_PublishesCompletedPersistsHistoryAndProjectsTwitch()
    {
        await _eventBus.PublishAsync(Online(), CancellationToken.None);

        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u2", "Bob"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u3", "Carol"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u3", "Carol"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromHours(2));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        Assert.That(_captured, Has.Count.EqualTo(1));

        var record = _captured[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Channel, Is.EqualTo(Channel));
            Assert.That(record.MessageCount, Is.EqualTo(6));
            Assert.That(record.ChatterCount, Is.EqualTo(3));
            Assert.That(record.PeakViewers, Is.EqualTo(10));
            Assert.That(record.AverageViewers, Is.EqualTo(10));
            Assert.That(record.Title, Is.EqualTo("Заголовок"));
            Assert.That(record.Game, Is.EqualTo("Игра"));
            Assert.That(record.Duration, Is.EqualTo(TimeSpan.FromHours(2)));
            Assert.That(record.EnumerateChatters().Select(c => c.DisplayName), Is.EqualTo(new[] { "Alice", "Carol", "Bob" }));
            Assert.That(record.EnumerateChatters().Select(c => c.MessageCount), Is.EqualTo(new long[] { 3, 2, 1 }));
            Assert.That(_store.Load(), Is.Null);
            Assert.That(_historyStore.Load().Broadcasts, Has.Count.EqualTo(1));
            Assert.That(_historyStore.LoadSessionsProjection().Sessions, Has.Count.EqualTo(1));
            _messenger.Received(1).SendToConnected(Arg.Any<string>(), ChatSendCategory.EventNotification);
        }
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task MultiPlatformBroadcast_AggregatesChattersSeparatelyAndProjectsOnlyTwitch(bool catchUp)
    {
        await _eventBus.PublishAsync(Online(viewers: 15), CancellationToken.None);
        await _eventBus.PublishAsync(OnlineYouTube(viewers: 25), CancellationToken.None);

        await _eventBus.PublishAsync(TwitchChat("shared", "TwitchUser"), CancellationToken.None);
        await _eventBus.PublishAsync(Chat(StreamingPlatform.YouTube, YouTubeChannel, "shared", "YouTubeUser"), CancellationToken.None);
        await _eventBus.PublishAsync(Chat(StreamingPlatform.YouTube, YouTubeChannel, "shared", "YouTubeUser"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: catchUp), CancellationToken.None);
        await WaitUntilAsync(() => !_aggregator.IsPlatformLive(StreamingPlatform.Twitch));

        Assert.That(_captured, Is.Empty);

        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        Assert.That(_captured, Has.Count.EqualTo(1));
        var record = _captured[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Platforms, Has.Count.EqualTo(2));
            Assert.That(record.MessageCount, Is.EqualTo(3));
            Assert.That(record.ChatterCount, Is.EqualTo(2));
            Assert.That(record.HasChatter(new PlatformUserKey(StreamingPlatform.Twitch, "shared")), Is.True);
            Assert.That(record.HasChatter(new PlatformUserKey(StreamingPlatform.YouTube, "shared")), Is.True);

            var projection = _historyStore.LoadSessionsProjection().Sessions.Single();
            Assert.That(projection.Id, Is.EqualTo(record.Id));
            Assert.That(projection.MessageCount, Is.EqualTo(1));
            Assert.That(projection.ChatterCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task YouTubeOnlyBroadcast_IsPersistedToBroadcastsButExcludedFromSessionsProjection()
    {
        await _eventBus.PublishAsync(OnlineYouTube(viewers: 40), CancellationToken.None);
        await _eventBus.PublishAsync(Chat(StreamingPlatform.YouTube, YouTubeChannel, "yt-1", "Viewer"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Has.Count.EqualTo(1));
            Assert.That(_historyStore.Load().Broadcasts, Has.Count.EqualTo(1));
            Assert.That(_historyStore.LoadSessionsProjection().Sessions, Is.Empty);
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task CatchUpAfterMonitorGap_ClosesPreviousStreamAtLastObservation(bool otherPlatformLive, bool rollover)
    {
        await _eventBus.PublishAsync(Online(streamId: "old-stream"));
        if (otherPlatformLive)
        {
            await _eventBus.PublishAsync(OnlineYouTube());
        }

        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, _timeProvider.GetUtcNow()));
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        var lastObservedAt = _timeProvider.GetUtcNow();
        await _eventBus.PublishAsync(new ViewerSampleObserved(StreamingPlatform.Twitch, Channel, 10, lastObservedAt, "old-stream"));
        await _eventBus.PublishAsync(new ChatIngestionStopped(StreamingPlatform.Twitch, lastObservedAt));
        _timeProvider.Advance(TimeSpan.FromMinutes(90));

        var completed = new TaskCompletionSource<BroadcastRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var completedSubscription = _eventBus.Subscribe<StreamSessionCompleted>(e => completed.TrySetResult(e.Broadcast));
        using var leftSubscription = _eventBus.Subscribe<BroadcastPlatformLeft>(_ => left.TrySetResult());
        if (rollover)
        {
            await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, new()
            {
                Id = "next-stream", StartedAt = _timeProvider.GetUtcNow().UtcDateTime,
            }, IsCatchUp: true));
        }
        else
        {
            await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: true));
        }

        if (otherPlatformLive)
        {
            await left.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (rollover)
            {
                await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));
            }

            await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel));
        }

        var record = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var twitch = record.Platforms.Single(platform => platform.Platform == StreamingPlatform.Twitch);
        Assert.That(twitch.Segments.First().EndedAt, Is.EqualTo(lastObservedAt));
        Assert.That(twitch.TrackedIntervals!.First().EndedAt, Is.EqualTo(lastObservedAt));
        if (!otherPlatformLive)
        {
            Assert.That(record.EndedAt, Is.EqualTo(lastObservedAt));
            Assert.That(record.ViewerSeries?.Any(sample => sample.At > record.EndedAt) ?? false, Is.False);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Restart_SecondaryPlatformRequiresItsOwnOnlineConfirmation(bool secondaryReturns)
    {
        var savedAt = StreamStart.AddMinutes(30);
        var draft = new ActiveBroadcast
        {
            BroadcastId = Guid.NewGuid(), StartedAt = StreamStart, UpdatedAt = savedAt,
            Platforms = [.. new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }.Select(platform => new ActiveBroadcastPlatform
            {
                Platform = platform, Channel = platform == StreamingPlatform.Twitch ? Channel : YouTubeChannel,
                StreamId = platform == StreamingPlatform.Twitch ? "saved-twitch" : "saved-youtube",
                StartedAt = StreamStart, UpdatedAt = savedAt,
                Segments = [new() { StartedAt = StreamStart, MessageCount = 9 }],
                TrackedIntervals = [new() { StartedAt = StreamStart }],
            })],
        };
        Assert.That(_store.TrySave(draft), Is.True);
        _timeProvider.Advance(TimeSpan.FromHours(2));
        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, _timeProvider.GetUtcNow()));
        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.YouTube, _timeProvider.GetUtcNow()));
        await _eventBus.PublishAsync(Online(streamId: "saved-twitch", isCatchUp: true));
        if (secondaryReturns)
        {
            await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.YouTube, YouTubeChannel,
                new() { Id = "saved-youtube", StartedAt = StreamStartUtc }, IsCatchUp: true));
            await _eventBus.PublishAsync(Chat(StreamingPlatform.YouTube, YouTubeChannel, "u1", "Bob"));
        }
        else
        {
            await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel, IsCatchUp: true));
        }

        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        var completed = new TaskCompletionSource<BroadcastRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _eventBus.Subscribe<StreamSessionCompleted>(e => completed.TrySetResult(e.Broadcast));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));
        if (secondaryReturns)
        {
            await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel));
        }

        var youtube = (await completed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Platforms.Single(platform => platform.Platform == StreamingPlatform.YouTube);
        Assert.That(youtube.EndedAt, Is.EqualTo(secondaryReturns ? _timeProvider.GetUtcNow() : savedAt));
        Assert.That(youtube.TrackedDuration, Is.EqualTo(TimeSpan.FromMinutes(secondaryReturns ? 60 : 30)));
        Assert.That(youtube.MessageCount, Is.EqualTo(secondaryReturns ? 10 : 9));
        Assert.That(youtube.Segments.Zip(youtube.Segments.Skip(1)).All(pair => pair.First.EndedAt <= pair.Second.StartedAt), Is.True);
    }

    [Test]
    public async Task CatchUpClosedPlatform_LateMetadataCannotReopenItsSegments()
    {
        await _eventBus.PublishAsync(Online(streamId: "twitch-stream"));
        await _eventBus.PublishAsync(OnlineYouTube());
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var leftSubscription = _eventBus.Subscribe<BroadcastPlatformLeft>(_ => left.TrySetResult());
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: true));
        await left.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closed = _store.Load()!.Platforms.Single(platform => platform.Platform == StreamingPlatform.Twitch);
        _timeProvider.Advance(TimeSpan.FromMinutes(90));
        await _eventBus.PublishAsync(new StreamMetadataResolved(StreamingPlatform.Twitch, Channel,
            new() { Id = "twitch-stream", StartedAt = StreamStartUtc, Title = "Позднее название", GameName = "Поздняя игра" }));

        var completed = new TaskCompletionSource<BroadcastRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _eventBus.Subscribe<StreamSessionCompleted>(e => completed.TrySetResult(e.Broadcast));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel));
        var twitch = (await completed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Platforms.Single(platform => platform.Platform == StreamingPlatform.Twitch);
        Assert.That(twitch.Segments, Has.Count.EqualTo(closed.Segments.Count));
        Assert.That(twitch.Segments.Single().EndedAt, Is.EqualTo(closed.EndedAt));
        Assert.That(twitch.Segments.Single().Title, Is.EqualTo(closed.Segments.Single().Title));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReconnectWithNewStreamId_ClosesPreviousPlatformBeforeStartingNext(bool otherPlatformLive)
    {
        await _eventBus.PublishAsync(Online(streamId: "old-stream"));
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"));
        if (otherPlatformLive)
        {
            await _eventBus.PublishAsync(OnlineYouTube());
        }

        var previousId = _aggregator.CurrentBroadcastId;
        _timeProvider.Advance(TimeSpan.FromHours(1));
        await _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, Channel, new()
        {
            Id = "next-stream", StartedAt = _timeProvider.GetUtcNow().UtcDateTime,
        }, IsCatchUp: true));
        Assert.That(_aggregator.CurrentBroadcastId == previousId, Is.EqualTo(otherPlatformLive));
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"));
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel));
        if (otherPlatformLive)
        {
            await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel));
        }

        await WaitUntilAsync(() => _captured.Count == (otherPlatformLive ? 1 : 2));
        Assert.That(_captured.Sum(record => record.MessageCount), Is.EqualTo(2));
        Assert.That(_captured.SelectMany(record => record.Platforms).Where(platform => platform.Platform == StreamingPlatform.Twitch)
            .Sum(platform => platform.MessageCount), Is.EqualTo(2));
    }

    [Test]
    public async Task ViewerSampleObserved_WithinInitialSeedDedupWindow_DoesNotDoubleCountInitialSample()
    {
        await _eventBus.PublishAsync(Online(viewers: 30, streamId: "stream-1"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        await _eventBus.PublishAsync(new ViewerSampleObserved(StreamingPlatform.Twitch, Channel, 30, _timeProvider.GetUtcNow(), "stream-1"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        await _eventBus.PublishAsync(new ViewerSampleObserved(StreamingPlatform.Twitch, Channel, 50, _timeProvider.GetUtcNow(), "stream-1"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var platform = _captured.Single().Platforms.Single();
        Assert.That(platform.PeakViewers, Is.EqualTo(50));
        Assert.That(platform.AverageViewers, Is.EqualTo(40));
    }

    [Test]
    public async Task SamplingLoop_RecordsCombinedMinuteSeriesAcrossLivePlatforms()
    {
        await _eventBus.PublishAsync(Online(viewers: 10), CancellationToken.None);
        await _eventBus.PublishAsync(OnlineYouTube(viewers: 20), CancellationToken.None);

        await _timeProvider.WaitForPendingTimersAsync(1);
        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => (_store.Load()?.ViewerSeries.Count ?? 0) >= 1);

        _timeProvider.Advance(TimeSpan.FromSeconds(20));
        await _eventBus.PublishAsync(new ViewerSampleObserved(StreamingPlatform.Twitch, Channel, 25, _timeProvider.GetUtcNow()), CancellationToken.None);
        await _eventBus.PublishAsync(new ViewerSampleObserved(StreamingPlatform.YouTube, YouTubeChannel, 35, _timeProvider.GetUtcNow()), CancellationToken.None);

        await _timeProvider.WaitForPendingTimersAsync(1);
        _timeProvider.Advance(TimeSpan.FromSeconds(40));
        await WaitUntilAsync(() => (_store.Load()?.ViewerSeries.Count ?? 0) >= 2);

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, YouTubeChannel), CancellationToken.None);
        await WaitUntilAsync(() => !_aggregator.IsAnyLive && _captured.Count == 1);

        var record = _captured.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.ViewerSeries, Is.Not.Null);
            Assert.That(record.ViewerSeries!.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(record.ViewerSeries[0].Viewers, Is.EqualTo(30));
            Assert.That(record.ViewerSeries[1].Viewers, Is.EqualTo(60));
            Assert.That(record.PeakViewers, Is.EqualTo(60));
        }
    }

    [Test]
    public async Task DebugChannelWithoutRecordsUserData_LeavesFilesAndExistingDraftUntouched()
    {
        SeedTwitchDraft(null, "keep-me", StreamStart.AddDays(-1), StreamStart.AddDays(-1).AddMinutes(10), 12);
        var beforeDraft = await File.ReadAllTextAsync(_activePath);

        _targetChannel.OwnChannel = "own-channel";
        _targetChannel.IsDebugSession = true;
        _targetChannel.Login = "debug-chan";
        await _eventBus.PublishAsync(Online(), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await Task.Delay(50);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Is.Empty);
            Assert.That(File.Exists(_broadcastsPath), Is.False);
            Assert.That(File.Exists(_sessionsPath), Is.False);
            Assert.That(await File.ReadAllTextAsync(_activePath), Is.EqualTo(beforeDraft));
            _messenger.DidNotReceive().SendToConnected(Arg.Any<string>(), Arg.Any<ChatSendCategory>());
        }
    }

    [Test]
    public async Task StreamWentOffline_WithIsCatchUpTrue_FinalizesLiveSession()
    {
        await _eventBus.PublishAsync(Online(), CancellationToken.None);
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: true), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(_aggregator.IsAnyLive, Is.False);
    }

    [Test]
    public async Task StreamWentOffline_NotificationsDisabled_StillPublishesAndSavesWithoutSendingMessage()
    {
        _settings.Twitch.AutoBroadcast.StreamStatusNotificationsEnabled = false;

        await _eventBus.PublishAsync(Online(), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(_historyStore.Load().Broadcasts, Has.Count.EqualTo(1));
        _messenger.DidNotReceive().SendToConnected(Arg.Any<string>(), Arg.Any<ChatSendCategory>());
    }

    [Test]
    public async Task ChatMessageReceived_FromBot_DoesNotCountTowardSession()
    {
        await _eventBus.PublishAsync(Online(), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("bot", "MyBot", isBot: true), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        Assert.That(_captured.Single().MessageCount, Is.EqualTo(1));
        Assert.That(_captured.Single().ChatterCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LegacyActiveSession_IsMigratedOneWayAndResumedAcrossRestart()
    {
        var legacy = new ActiveStreamSession
        {
            Channel = Channel,
            StreamId = "stream-legacy",
            StartedAt = StreamStart,
            UpdatedAt = StreamStart.AddHours(1),
            MessageCount = 100,
            PeakViewers = 50,
            ViewerSamplesSum = 300,
            ViewerSamplesCount = 10,
            Title = "Черновой заголовок",
            Game = "Черновая игра",
            Chatters =
            [
                new() { UserId = "u1", DisplayName = "Alice", MessageCount = 60 },
                new() { UserId = "u2", DisplayName = "Bob", MessageCount = 40 },
            ],
        };
        await File.WriteAllTextAsync(_legacyActivePath, JsonSerializer.Serialize(legacy, JsonStoreOptions.Default));

        await _handler.DisposeAsync();
        _aggregator.Dispose();
        _store = new(_historyStore, NullLogger<ActiveBroadcastStore>.Instance, _activePath, _legacyActivePath);
        _draftCoordinator = new(_historyStore, _store, _targetChannel, _timeProvider, NullLogger<BroadcastDraftCoordinator>.Instance);
        _aggregator = new(_eventBus, _draftCoordinator, _targetChannel, _timeProvider, NullLogger<BroadcastAggregator>.Instance);
        _handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(70));
        await _eventBus.PublishAsync(Online(title: "Черновой заголовок", game: "Черновая игра", streamId: "stream-legacy", isCatchUp: true), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u3", "Carol"), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(20));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var record = _captured.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Id, Is.EqualTo(ActiveBroadcastStore.ComputeDeterministicBroadcastId(legacy)));
            Assert.That(record.MessageCount, Is.EqualTo(101));
            Assert.That(record.ChatterCount, Is.EqualTo(3));
            Assert.That(File.Exists(_legacyActivePath), Is.False);
            Assert.That(_store.Load(), Is.Null);
        }
    }

    [Test]
    public async Task CrashBetweenHistoryWriteAndDraftDelete_StartupRecoveryDeletesDraftWithoutDuplicatingHistory()
    {
        await _eventBus.PublishAsync(Online(), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var completedRecord = _captured.Single();

        _store.TrySave(new ActiveBroadcast
        {
            BroadcastId = completedRecord.Id,
            StartedAt = completedRecord.StartedAt,
            UpdatedAt = completedRecord.EndedAt,
            EndedAt = completedRecord.EndedAt,
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = Channel,
                    StartedAt = completedRecord.StartedAt,
                    UpdatedAt = completedRecord.EndedAt,
                    EndedAt = completedRecord.EndedAt,
                    Segments =
                    [
                        new ActiveStreamSessionSegment
                        {
                            StartedAt = completedRecord.StartedAt,
                            EndedAt = completedRecord.EndedAt,
                            MessageCount = completedRecord.MessageCount,
                        },
                    ],
                },
            ],
        });

        _draftCoordinator.RecoverCompletedDraftOnStartup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_historyStore.Load().Broadcasts, Has.Count.EqualTo(1));
            Assert.That(_store.Load(), Is.Null);
            Assert.That(File.Exists(_activePath), Is.False);
        }
    }

    [Test]
    public async Task StreamWentOnline_CatchUp_WithDifferentStreamDraft_FinalizesOldAndStartsFresh()
    {
        var oldId = Guid.NewGuid();
        var oldStart = StreamStart.AddDays(-2);
        SeedTwitchDraft(oldId, "old-stream", oldStart, oldStart.AddHours(2), 77, ("u9", "Veteran", 77));

        await _eventBus.PublishAsync(Online(streamId: "new-stream", isCatchUp: true), CancellationToken.None);

        Assert.That(_captured, Has.Count.EqualTo(1));
        Assert.That(_captured[0].Id, Is.EqualTo(oldId));
        Assert.That(_captured[0].MessageCount, Is.EqualTo(77));

        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 2);

        Assert.That(_captured, Has.Count.EqualTo(2));
        Assert.That(_captured[1].Id, Is.Not.EqualTo(oldId));
        Assert.That(_captured[1].MessageCount, Is.EqualTo(0));
    }

    [Test]
    public async Task StreamWentOffline_CatchUp_WithoutLiveSession_FinalizesOrphanDraftFromDisk()
    {
        var orphanId = Guid.NewGuid();
        SeedTwitchDraft(orphanId, "orphan-stream", StreamStart, StreamStart.AddHours(4), 55, ("u1", "Alice", 30), ("u2", "Bob", 25));

        _timeProvider.Advance(TimeSpan.FromHours(5));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: true), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_captured, Has.Count.EqualTo(1));
            Assert.That(_captured[0].Id, Is.EqualTo(orphanId));
            Assert.That(_captured[0].MessageCount, Is.EqualTo(55));
            Assert.That(_store.Load(), Is.Null);
        }
    }

    [Test]
    public async Task ChannelUpdated_MidStream_SplitsSegmentsAndPreservesAcrossRestart()
    {
        await _eventBus.PublishAsync(Online(title: "Первая часть", game: "Игра А", streamId: "seg-stream"), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u1", "Alice"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(30));
        await _eventBus.PublishAsync(new ChannelUpdated(StreamingPlatform.Twitch, "Вторая часть", "ru", "game-id", "Игра Б", []), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u2", "Bob"), CancellationToken.None);

        await _handler.DisposeAsync();
        _aggregator.Dispose();

        _aggregator = new(_eventBus, _draftCoordinator, _targetChannel, _timeProvider, NullLogger<BroadcastAggregator>.Instance);
        _handler = CreateHandler();

        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(Online(title: "Вторая часть", game: "Игра Б", streamId: "seg-stream", isCatchUp: true), CancellationToken.None);
        await _eventBus.PublishAsync(TwitchChat("u2", "Bob"), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var platform = _captured.Single().Platforms.Single();
        Assert.That(platform.Segments, Has.Count.EqualTo(2));
        Assert.That(platform.Segments[0].MessageCount, Is.EqualTo(1));
        Assert.That(platform.Segments[1].MessageCount, Is.EqualTo(2));
        Assert.That(platform.Segments[1].Game, Is.EqualTo("Игра Б"));
    }

    [Test]
    public async Task TrackedIntervals_TrimmedToStreamAndSplitAcrossReconnect()
    {
        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, StreamStart.AddMinutes(-30)), CancellationToken.None);
        await _eventBus.PublishAsync(Online(), CancellationToken.None);

        await _eventBus.PublishAsync(new ChatIngestionStopped(StreamingPlatform.Twitch, StreamStart.AddMinutes(20)), CancellationToken.None);
        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, StreamStart.AddMinutes(25)), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(79));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);
        await _eventBus.PublishAsync(new ChatIngestionStopped(StreamingPlatform.Twitch, StreamStart.AddMinutes(90)), CancellationToken.None);

        var platform = _captured.Single().Platforms.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(platform.TrackedIntervals!.Select(interval => (interval.StartedAt, interval.EndedAt)),
                Is.EqualTo(new[]
                {
                    (StreamStart, StreamStart.AddMinutes(20)),
                    (StreamStart.AddMinutes(25), StreamStart.AddMinutes(79)),
                }));

            Assert.That(platform.TrackedDuration, Is.EqualTo(TimeSpan.FromMinutes(74)));
        }
    }

    [Test]
    public async Task TrackedIntervals_LegacyNullPreservedOnResume()
    {
        SeedTwitchDraft(null, "legacy-null", StreamStart, StreamStart.AddMinutes(30), 5);

        await _eventBus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, StreamStart.AddMinutes(31)), CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromMinutes(31));
        await _eventBus.PublishAsync(Online(streamId: "legacy-null", isCatchUp: true), CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(29));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var platform = _captured.Single().Platforms.Single();
        Assert.That(platform.TrackedIntervals, Is.Null);
        Assert.That(platform.TrackedDuration, Is.Null);
    }

    [Test]
    public async Task TrackedIntervals_OrphanDraftClosedByUpdatedAt()
    {
        _store.TrySave(new ActiveBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            StartedAt = StreamStart,
            UpdatedAt = StreamStart.AddMinutes(30),
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = Channel,
                    StartedAt = StreamStart,
                    UpdatedAt = StreamStart.AddMinutes(30),
                    Segments = [new() { StartedAt = StreamStart, Title = "Заголовок", Game = "Игра" }],
                    TrackedIntervals = [new() { StartedAt = StreamStart.AddMinutes(5) }],
                },
            ],
        });

        _timeProvider.Advance(TimeSpan.FromHours(2));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, Channel, IsCatchUp: true), CancellationToken.None);
        await WaitUntilAsync(() => _captured.Count == 1);

        var orphan = _captured.Single();
        Assert.That(orphan.Duration, Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(orphan.Platforms.Single().TrackedDuration, Is.EqualTo(TimeSpan.FromMinutes(25)));
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
