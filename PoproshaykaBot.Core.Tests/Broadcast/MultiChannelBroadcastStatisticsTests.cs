using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
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
public sealed class MultiChannelBroadcastStatisticsTests
{
    private string _directory = null!;
    private ManualTimeProvider _time = null!;
    private InMemoryEventBus _eventBus = null!;
    private BroadcastHistoryStore _historyStore = null!;
    private ActiveBroadcastStore _activeStore = null!;
    private BroadcastDraftCoordinator _drafts = null!;
    private BroadcastAggregator _aggregator = null!;
    private StreamSessionStatisticsHandler _handler = null!;
    private List<BroadcastRecord> _captured = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-mc-broadcast-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _time = new();
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _captured = [];
        _eventBus.Subscribe<StreamSessionCompleted>(e => _captured.Add(e.Broadcast));

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
    public async Task Sequential_YouTube_channels_during_live_Twitch_preserve_both_intervals_and_colliding_stream_ids()
    {
        var t0 = _time.GetUtcNow().UtcDateTime;

        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.Twitch,
            "tw_main",
            new StreamInfo { Id = "shared-stream-id", StartedAt = t0, Title = "TW" },
            ChannelId: "tw_100"));

        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "YouTube A",
            new StreamInfo { Id = "shared-stream-id", StartedAt = t0, Title = "YT A" },
            ChannelId: "UC_yt_a"));

        await _eventBus.PublishAsync(ChatMessage(StreamingPlatform.Twitch, "tw_main", "tw_100", "u1", "TwViewer"));
        await _eventBus.PublishAsync(ChatMessage(StreamingPlatform.YouTube, "YouTube A", "UC_yt_a", "u2", "YtViewerA"));

        _time.Advance(TimeSpan.FromMinutes(20));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "YouTube A", ChannelId: "UC_yt_a"));

        _time.Advance(TimeSpan.FromMinutes(10));
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube,
            "YouTube B",
            new StreamInfo { Id = "shared-stream-id", StartedAt = t0.AddMinutes(30), Title = "YT B" },
            ChannelId: "UC_yt_b"));

        await _eventBus.PublishAsync(ChatMessage(StreamingPlatform.YouTube, "YouTube B", "UC_yt_b", "u3", "YtViewerB"));

        _time.Advance(TimeSpan.FromMinutes(15));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "YouTube B", ChannelId: "UC_yt_b"));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "tw_main", ChannelId: "tw_100"));

        await WaitUntilAsync(() => _captured.Count == 1);

        var broadcast = _captured.Single();
        var youtubeRecords = broadcast.Platforms.Where(static p => p.Platform == StreamingPlatform.YouTube).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(broadcast.Platforms, Has.Count.EqualTo(3));
            Assert.That(youtubeRecords.Select(static p => p.ChannelId), Is.EquivalentTo(new[] { "UC_yt_a", "UC_yt_b" }));
            Assert.That(youtubeRecords.Single(static p => p.ChannelId == "UC_yt_a").MessageCount, Is.EqualTo(1L));
            Assert.That(youtubeRecords.Single(static p => p.ChannelId == "UC_yt_b").MessageCount, Is.EqualTo(1L));
        }
    }

    [Test]
    public void BroadcastHistoryMerge_keeps_different_channels_of_same_platform_separate_in_one_broadcast()
    {
        var start = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        var b1 = new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start,
            EndedAt = start.AddMinutes(30),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "tw_main",
                    ChannelId = "tw_100",
                    StartedAt = start,
                    EndedAt = start.AddMinutes(30),
                    MessageCount = 2,
                    Chatters = [new StreamSessionChatter { UserId = "tw1", DisplayName = "TW1", MessageCount = 2 }],
                },
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.YouTube,
                    Channel = "YT A",
                    ChannelId = "UC_a",
                    StartedAt = start,
                    EndedAt = start.AddMinutes(30),
                    MessageCount = 4,
                    Chatters = [new StreamSessionChatter { UserId = "u1", DisplayName = "U1", MessageCount = 4 }],
                },
            ],
        };
        var b2 = new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start.AddMinutes(1),
            EndedAt = start.AddMinutes(40),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "tw_main",
                    ChannelId = "tw_100",
                    StartedAt = start.AddMinutes(1),
                    EndedAt = start.AddMinutes(40),
                    MessageCount = 3,
                    Chatters = [new StreamSessionChatter { UserId = "tw1", DisplayName = "TW1", MessageCount = 3 }],
                },
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.YouTube,
                    Channel = "YT B",
                    ChannelId = "UC_b",
                    StartedAt = start.AddMinutes(1),
                    EndedAt = start.AddMinutes(40),
                    MessageCount = 7,
                    Chatters = [new StreamSessionChatter { UserId = "u2", DisplayName = "U2", MessageCount = 7 }],
                },
            ],
        };

        var merged = BroadcastHistoryMerge.Merge([b1, b2]).Broadcasts;

        Assert.That(merged, Has.Count.EqualTo(1));
        Assert.That(
            merged[0].Platforms.Where(static p => p.Platform == StreamingPlatform.YouTube).Select(static p => p.ChannelId),
            Is.EquivalentTo(new[] { "UC_a", "UC_b" }));
    }

    [Test]
    public void BroadcastHistoryStore_writes_pre_multichannel_backup_and_preserves_rollback_on_separate_copy()
    {
        var start = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        var legacyBroadcastsPath = Path.Combine(_directory, "broadcasts.json");
        var legacySessionsPath = Path.Combine(_directory, "stream_sessions.json");

        var legacyStore = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, legacyBroadcastsPath, legacySessionsPath);
        legacyStore.TryAppend(BroadcastRecord.FromTwitchSession(new StreamSessionRecord
        {
            Channel = "tw_main",
            StartedAt = start,
            EndedAt = start.AddHours(1),
            MessageCount = 10,
            ChatterCount = 1,
            Chatters = [new StreamSessionChatter { UserId = "u1", DisplayName = "U1", MessageCount = 10 }],
        }));

        legacyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start.AddDays(1),
            EndedAt = start.AddDays(1).AddHours(1),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "tw_main",
                    ChannelId = "1001",
                    StartedAt = start.AddDays(1),
                    EndedAt = start.AddDays(1).AddHours(1),
                    MessageCount = 5,
                    Chatters = [new StreamSessionChatter { UserId = "u1", DisplayName = "U1", MessageCount = 5 }],
                },
            ],
        });

        var broadcastsBackup = Path.Combine(_directory, "broadcasts.pre-multichannel.json");
        var sessionsBackup = Path.Combine(_directory, "stream_sessions.pre-multichannel.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(broadcastsBackup), Is.True);
            Assert.That(File.Exists(sessionsBackup), Is.True);
        }

        var rollbackDir = Path.Combine(_directory, "rollback");
        Directory.CreateDirectory(rollbackDir);
        File.Copy(broadcastsBackup, Path.Combine(rollbackDir, "broadcasts.json"));
        File.Copy(sessionsBackup, Path.Combine(rollbackDir, "stream_sessions.json"));

        var restoredStore = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(rollbackDir, "broadcasts.json"),
            Path.Combine(rollbackDir, "stream_sessions.json"));
        var loaded = restoredStore.Load();

        Assert.That(loaded.Broadcasts, Has.Count.EqualTo(1));
        Assert.That(loaded.Broadcasts[0].Platforms[0].ChannelId, Is.Null);
    }

    [Test]
    public async Task Late_offline_for_superseded_channel_does_not_stop_active_channel_and_reconnect_adds_new_segment()
    {
        var t0 = _time.GetUtcNow().UtcDateTime;

        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.Twitch, "tw_main", new StreamInfo { Id = "tw-1", StartedAt = t0 }, ChannelId: "tw_100"));
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube, "YouTube A", new StreamInfo { Id = "yt-a-1", StartedAt = t0 }, ChannelId: "UC_yt_a"));
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube, "YouTube B", new StreamInfo { Id = "yt-b-1", StartedAt = t0.AddMinutes(5) }, ChannelId: "UC_yt_b"));

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "YouTube A", ChannelId: "UC_yt_a"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_aggregator.IsPlatformLive(StreamingPlatform.YouTube), Is.True);
            Assert.That(_aggregator.IsAnyLive, Is.True);
            Assert.That(_captured, Is.Empty);
        }

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "YouTube B", ChannelId: "UC_yt_b"));

        _time.Advance(TimeSpan.FromMinutes(10));
        await _eventBus.PublishAsync(new StreamWentOnline(
            StreamingPlatform.YouTube, "YouTube B", new StreamInfo { Id = "yt-b-2", StartedAt = t0.AddMinutes(15) }, ChannelId: "UC_yt_b"));
        await _eventBus.PublishAsync(ChatMessage(StreamingPlatform.YouTube, "YouTube B", "UC_yt_b", "u9", "FreshUser"));

        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "YouTube B", ChannelId: "UC_yt_b"));
        await _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "tw_main", ChannelId: "tw_100"));
        await WaitUntilAsync(() => _captured.Count == 1);

        var ytBCollector = _captured[0].Platforms.Single(p => p.ChannelId == "UC_yt_b");
        Assert.That(ytBCollector.Segments, Has.Count.EqualTo(2));
    }

    [Test]
    public void Reconciliation_with_stale_multi_row_projection_preserves_counts_hidden_state_and_guards_backup_failure()
    {
        var start = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
        var b1Id = Guid.NewGuid();
        var secondProjId = Guid.NewGuid();

        var doc = new BroadcastHistoryDocument
        {
            SchemaVersion = BroadcastHistoryDocument.CurrentSchemaVersion,
            ProjectionOutOfDate = true,
            Broadcasts =
            [
                new BroadcastRecord
                {
                    Id = b1Id,
                    MergedBroadcastIds = [secondProjId],
                    StartedAt = start,
                    EndedAt = start.AddHours(2),
                    Platforms =
                    [
                        new BroadcastPlatformRecord
                        {
                            Platform = StreamingPlatform.Twitch,
                            Channel = "tw_main",
                            ChannelId = "1001",
                            StartedAt = start,
                            MergedStartedAts = [start.AddHours(1)],
                            EndedAt = start.AddHours(2),
                            MessageCount = 30,
                            ChatterCount = 1,
                            Chatters = [new StreamSessionChatter { UserId = "u1", DisplayName = "Alice", MessageCount = 30 }],
                        },
                    ],
                },
            ],
        };

        var staleProjection = new StreamSessionHistory
        {
            Sessions =
            [
                new StreamSessionRecord { Id = b1Id, Channel = "tw_main", StartedAt = start, EndedAt = start.AddMinutes(50), MessageCount = 10, IsHidden = true },
                new StreamSessionRecord { Id = secondProjId, Channel = "tw_main", StartedAt = start.AddHours(1), EndedAt = start.AddHours(2), MessageCount = 20, IsHidden = false },
            ],
        };

        var dir = Path.Combine(_directory, "stale-reconcile");
        Directory.CreateDirectory(dir);
        var opts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        File.WriteAllText(Path.Combine(dir, "broadcasts.json"), System.Text.Json.JsonSerializer.Serialize(doc, opts));
        File.WriteAllText(Path.Combine(dir, "stream_sessions.json"), System.Text.Json.JsonSerializer.Serialize(staleProjection, opts));

        var store = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(dir, "broadcasts.json"),
            Path.Combine(dir, "stream_sessions.json"));
        var loaded = store.Load().Broadcasts.Single();
        Assert.That(loaded.MessageCount, Is.EqualTo(30));

        // Verify order-independent hiding when second projection row has IsHidden = false while first had IsHidden = true
        var hiddenDir = Path.Combine(_directory, "order-hidden");
        Directory.CreateDirectory(hiddenDir);
        doc.ProjectionOutOfDate = false;
        doc.Broadcasts[0].MergedBroadcastIds = null;
        doc.Broadcasts[0].Platforms.Add(new BroadcastPlatformRecord
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "tw_alt",
            ChannelId = "1002",
            ProjectionSessionId = secondProjId,
            StartedAt = start.AddHours(1),
            EndedAt = start.AddHours(2),
            MessageCount = 20,
        });
        File.WriteAllText(Path.Combine(hiddenDir, "broadcasts.json"), System.Text.Json.JsonSerializer.Serialize(doc, opts));
        File.WriteAllText(Path.Combine(hiddenDir, "stream_sessions.json"), System.Text.Json.JsonSerializer.Serialize(staleProjection, opts));

        var hiddenStore = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(hiddenDir, "broadcasts.json"),
            Path.Combine(hiddenDir, "stream_sessions.json"));
        Assert.That(hiddenStore.Load().Broadcasts.Single().IsHidden, Is.True);

        // Verify backup failure prevents overwriting pre-multichannel source
        var blockedDir = Path.Combine(_directory, "blocked-backup");
        Directory.CreateDirectory(blockedDir);
        var legacySource = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(blockedDir, "broadcasts.json"),
            Path.Combine(blockedDir, "stream_sessions.json"));
        legacySource.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start,
            EndedAt = start.AddHours(1),
            Platforms = [new BroadcastPlatformRecord { Platform = StreamingPlatform.Twitch, Channel = "tw_main", StartedAt = start, EndedAt = start.AddHours(1), MessageCount = 5 }],
        });
        Directory.CreateDirectory(Path.Combine(blockedDir, "broadcasts.pre-multichannel.json"));
        var originalJson = File.ReadAllText(Path.Combine(blockedDir, "broadcasts.json"));

        var blockedAppend = legacySource.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start.AddDays(1),
            EndedAt = start.AddDays(1).AddHours(1),
            Platforms = [new BroadcastPlatformRecord { Platform = StreamingPlatform.Twitch, Channel = "tw_main", ChannelId = "1001", StartedAt = start.AddDays(1), EndedAt = start.AddDays(1).AddHours(1), MessageCount = 7 }],
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(blockedAppend, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(blockedDir, "broadcasts.json")), Is.EqualTo(originalJson));
        }
    }

    private static ChatMessageReceived ChatMessage(
        StreamingPlatform platform,
        string channel,
        string channelId,
        string userId,
        string displayName)
    {
        return new(
            platform,
            channel,
            Guid.NewGuid().ToString("N"),
            userId,
            displayName.ToLowerInvariant(),
            displayName,
            "сообщение",
            UserStatus.None,
            false,
            new() { Platform = platform, ChannelId = channelId },
            Origin: new(new(platform, channelId), channel));
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

        Assert.Fail("Timed out waiting for async broadcast completion.");
    }
}
