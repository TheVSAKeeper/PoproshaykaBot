using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class ActiveBroadcastStoreTests
{
    private string _directory = null!;
    private string _broadcastsPath = null!;
    private string _sessionsPath = null!;
    private string _activePath = null!;
    private string _legacyPath = null!;
    private BroadcastHistoryStore _historyStore = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PoproshaykaBot-ActiveBroadcast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _broadcastsPath = Path.Combine(_directory, "broadcasts.json");
        _sessionsPath = Path.Combine(_directory, "stream_sessions.json");
        _activePath = Path.Combine(_directory, "active_broadcast.json");
        _legacyPath = Path.Combine(_directory, "active_session.json");
        _historyStore = new(NullLogger<BroadcastHistoryStore>.Instance, _broadcastsPath, _sessionsPath);
    }

    private ActiveBroadcastStore CreateStore()
    {
        return new(_historyStore, NullLogger<ActiveBroadcastStore>.Instance, _activePath, _legacyPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Test]
    public void MissingFile_SetsMissingReadStateAndAllowsSave()
    {
        var store = CreateStore();

        Assert.That(store.ReadState, Is.EqualTo(FileReadState.Missing));
        Assert.That(store.CanWrite, Is.True);
        Assert.That(store.Load(), Is.Null);

        var draft = new ActiveBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.That(store.TrySave(draft), Is.True);
        Assert.That(store.Load()?.BroadcastId, Is.EqualTo(draft.BroadcastId));
    }

    [Test]
    public void CorruptFile_SetsFailedReadStateCreatesInvalidBackupAndBlocksSave()
    {
        File.WriteAllText(_activePath, "{broken-json");
        var store = CreateStore();

        Assert.That(store.Load(), Is.Null);
        Assert.That(store.ReadState, Is.EqualTo(FileReadState.Failed));
        Assert.That(Directory.GetFiles(_directory, "active_broadcast.invalid-*"), Has.Length.EqualTo(1));

        var attempted = store.TrySave(new ActiveBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        Assert.That(attempted, Is.False);
        Assert.That(File.ReadAllText(_activePath), Is.EqualTo("{broken-json"));
    }

    [Test]
    public void Load_MigratesLegacyActiveSessionOneWayWithDeterministicBroadcastId()
    {
        var start = new DateTimeOffset(2026, 11, 10, 14, 0, 0, TimeSpan.Zero);
        var legacy = new ActiveStreamSession
        {
            Channel = "twitch-chan",
            StreamId = "stream-42",
            StartedAt = start,
            UpdatedAt = start.AddHours(1),
            MessageCount = 15,
            PeakViewers = 25,
            ViewerSamplesSum = 80,
            ViewerSamplesCount = 4,
            Title = "Legacy Title",
            Game = "Legacy Game",
            Chatters =
            [
                new() { UserId = "u1", DisplayName = "Alice", MessageCount = 15 },
            ],
        };

        File.WriteAllText(_legacyPath, JsonSerializer.Serialize(legacy, JsonStoreOptions.Default));
        var store = CreateStore();

        var expectedId = ActiveBroadcastStore.ComputeDeterministicBroadcastId(legacy);
        var migrated = store.Load();

        Assert.That(migrated, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.CanWrite, Is.True);
            Assert.That(migrated!.BroadcastId, Is.EqualTo(expectedId));
            Assert.That(migrated.Platforms, Has.Count.EqualTo(1));
            Assert.That(migrated.Platforms[0].Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(migrated.Platforms[0].Channel, Is.EqualTo("twitch-chan"));
            Assert.That(migrated.Platforms[0].Segments.Sum(s => s.MessageCount), Is.EqualTo(15));
            Assert.That(File.Exists(_activePath), Is.True);
            Assert.That(File.Exists(_legacyPath), Is.False);
        }
    }

    [Test]
    public void CorruptLegacyFile_CreatesInvalidBackupAndDoesNotMigrate()
    {
        File.WriteAllText(_legacyPath, "{not-valid");
        var store = CreateStore();

        Assert.That(store.Load(), Is.Null);
        Assert.That(store.CanWrite, Is.True);
        Assert.That(Directory.GetFiles(_directory, "active_session.invalid-*"), Has.Length.EqualTo(1));
        Assert.That(File.Exists(_activePath), Is.False);
    }

    [Test]
    public void CompletedDraft_NotInHistory_BlocksOverwriteUntilPersistedThenRecoverCleansUp()
    {
        var store = CreateStore();
        var completedId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
        var completed = new ActiveBroadcast
        {
            BroadcastId = completedId,
            StartedAt = start,
            UpdatedAt = start.AddHours(1),
            EndedAt = start.AddHours(1),
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "twitch-chan",
                    StartedAt = start,
                    UpdatedAt = start.AddHours(1),
                    EndedAt = start.AddHours(1),
                    Segments = [new() { StartedAt = start, EndedAt = start.AddHours(1), MessageCount = 9 }],
                },
            ],
        };

        Assert.That(store.TrySave(completed), Is.True);

        var other = new ActiveBroadcast
        {
            BroadcastId = Guid.NewGuid(),
            StartedAt = start.AddHours(2),
            UpdatedAt = start.AddHours(2),
        };

        Assert.That(store.TrySave(other), Is.False);
        Assert.That(store.Load()?.BroadcastId, Is.EqualTo(completedId));

        var coordinator = new BroadcastDraftCoordinator(
            _historyStore,
            store,
            new FakeTargetChannelProvider("twitch-chan"),
            new ManualTimeProvider(),
            NullLogger<BroadcastDraftCoordinator>.Instance);

        coordinator.RecoverCompletedDraftOnStartup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_historyStore.IsBroadcastPersistedOnDisk(completedId), Is.True);
            Assert.That(_historyStore.Load().Broadcasts.Select(b => b.Id), Contains.Item(completedId));
            Assert.That(File.Exists(_activePath), Is.False);
            Assert.That(store.Load(), Is.Null);
        }
    }
}
