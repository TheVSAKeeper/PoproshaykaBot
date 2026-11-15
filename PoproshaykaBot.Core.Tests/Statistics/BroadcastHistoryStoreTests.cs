using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed partial class BroadcastHistoryStoreTests
{
    private string _directory = null!;
    private string _broadcastsPath = null!;
    private string _sessionsPath = null!;
    private BroadcastHistoryStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"broadcast-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _broadcastsPath = Path.Combine(_directory, "broadcasts.json");
        _sessionsPath = Path.Combine(_directory, "stream_sessions.json");
        _store = new(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private static BroadcastRecord CreateTwitchRecord(string channel, long messages, DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        return BroadcastRecord.FromTwitchSession(new()
        {
            Channel = channel,
            StartedAt = start,
            EndedAt = start.AddHours(2),
            Title = "Заголовок",
            Game = "Игра",
            MessageCount = messages,
            ChatterCount = 2,
            PeakViewers = 5,
            AverageViewers = 3,
            Chatters =
            [
                new() { UserId = "u1", DisplayName = "Alice", MessageCount = 3 },
                new() { UserId = "u2", DisplayName = "Bob", MessageCount = 1 },
            ],
        });
    }

    private static BroadcastRecord CreateYouTubeOnlyRecord(string channel, long messages, DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
        var platform = new BroadcastPlatformRecord
        {
            Platform = StreamingPlatform.YouTube,
            Channel = channel,
            StartedAt = start,
            EndedAt = start.AddHours(1),
            Title = "YT Live",
            MessageCount = messages,
            ChatterCount = 1,
            PeakViewers = 20,
            AverageViewers = 15,
            Chatters = [new() { UserId = "yt-1", DisplayName = "YTV", MessageCount = messages }],
        };
        platform.EnsureSegments();

        var record = new BroadcastRecord
        {
            StartedAt = start,
            EndedAt = start.AddHours(1),
            PeakViewers = 20,
            AverageViewers = 15,
            ViewerSeries = [new(start, 15), new(start.AddMinutes(30), 20)],
            Platforms = [platform],
        };
        record.EnsureSegments();
        return record;
    }

    [Test]
    public void TryAppend_PersistsSourceAndProjectsOnlyTwitchToSessionsFile()
    {
        var twitch = CreateTwitchRecord("tw-chan", 10);
        var youtubeOnly = CreateYouTubeOnlyRecord("yt-chan", 25);

        Assert.That(_store.TryAppend(twitch), Is.True);
        Assert.That(_store.TryAppend(youtubeOnly), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_store.Load().Broadcasts, Has.Count.EqualTo(2));
            Assert.That(File.Exists(_broadcastsPath), Is.True);
            Assert.That(File.Exists(_sessionsPath), Is.True);

            var projection = _store.LoadSessionsProjection();
            Assert.That(projection.Sessions, Has.Count.EqualTo(1));
            Assert.That(projection.Sessions[0].Id, Is.EqualTo(twitch.Id));
            Assert.That(projection.Sessions[0].Channel, Is.EqualTo("tw-chan"));
        }
    }

    [Test]
    public void TryAppend_DoesNotShareReferenceWithCaller()
    {
        var record = CreateTwitchRecord("chan", 10);
        Assert.That(_store.TryAppend(record), Is.True);

        record.Platforms[0].MessageCount = 999;

        Assert.That(_store.Load().Broadcasts[0].MessageCount, Is.EqualTo(10));
    }

    [Test]
    public void Reload_RestoresEntriesAndPreservesMultiplatformChatters()
    {
        var tw = CreateTwitchRecord("tw", 10);
        var yt = CreateYouTubeOnlyRecord("yt", 5);
        _store.TryAppend(tw);
        _store.TryAppend(yt);

        var reloaded = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
        var broadcasts = reloaded.Load().Broadcasts;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(broadcasts, Has.Count.EqualTo(2));
            Assert.That(broadcasts[0].Platforms[0].Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(broadcasts[1].Platforms[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
        }
    }

    [Test]
    public void CorruptBroadcastsFile_CreatesBackupAndBlocksWritesUntilValid()
    {
        File.WriteAllText(_broadcastsPath, "{ это невалидный json");
        var store = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);

        Assert.That(store.Load().Broadcasts, Is.Empty);
        Assert.That(store.SourceReadState, Is.EqualTo(FileReadState.Failed));
        Assert.That(Directory.GetFiles(_directory, "broadcasts.invalid-*"), Has.Length.EqualTo(1));
        Assert.That(store.TryAppend(CreateTwitchRecord("chan", 5)), Is.False);
        Assert.That(File.ReadAllText(_broadcastsPath), Is.EqualTo("{ это невалидный json"));
    }

    [Test]
    public void Load_WhenOnlyLegacySessionsFileExists_MigratesOneTimeIntoBroadcastsFile()
    {
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var legacy = new StreamSessionHistory
        {
            Sessions =
            [
                new StreamSessionRecord
                {
                    Channel = "legacy-chan",
                    StartedAt = start,
                    EndedAt = start.AddHours(2),
                    MessageCount = 42,
                    Title = "Старый эфир",
                },
            ],
        };
        File.WriteAllText(_sessionsPath, JsonSerializer.Serialize(legacy, JsonStoreOptions.Default));

        var store = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
        var loaded = store.Load();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Broadcasts, Has.Count.EqualTo(1));
            Assert.That(loaded.Broadcasts[0].Channel, Is.EqualTo("legacy-chan"));
            Assert.That(loaded.Broadcasts[0].MessageCount, Is.EqualTo(42));
            Assert.That(File.Exists(_broadcastsPath), Is.True);
        }
    }

    [Test]
    public void TrySetHidden_UpdatesBothFilesAndRollsBackOnWriteFailure()
    {
        var record = CreateTwitchRecord("chan", 10);
        _store.TryAppend(record);

        Assert.That(_store.TrySetHidden(record.Id, true), Is.True);

        var reloaded = new BroadcastHistoryStore(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
        Assert.That(reloaded.Load().Broadcasts.Single().IsHidden, Is.True);
        Assert.That(reloaded.LoadVisible().Broadcasts, Is.Empty);
        Assert.That(reloaded.LoadSessionsProjection().Sessions.Single().IsHidden, Is.True);

        Directory.CreateDirectory(_broadcastsPath + ".tmp");
        Assert.That(reloaded.TrySetHidden(record.Id, false), Is.False);
        Assert.That(reloaded.Load().Broadcasts.Single().IsHidden, Is.True);
    }

    [Test]
    public void TrySetHidden_UnknownId_ReturnsFalse()
    {
        _store.TryAppend(CreateTwitchRecord("chan", 10));

        Assert.That(_store.TrySetHidden(Guid.NewGuid(), true), Is.False);
    }
}
