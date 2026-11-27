using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatHistoryCacheStoreTests
{
    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNowValue;
    }

    private DirectoryInfo _directory = null!;
    private string _cacheDir = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-history-cache-store");
        _cacheDir = Path.Combine(_directory.FullName, "chat-history");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public async Task Persistence_SurvivesRestart_PublishesChatHistoryLoaded_AndLeavesNoBakFiles()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        using (var store = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock))
        using (var manager = CreateManager(settingsPath, chatPath, store, clock, out _))
        {
            manager.AddMessage(new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "SecretChannelName",
                ChannelId = "4242",
                MessageId = "tw-1",
                Timestamp = clock.UtcNowValue.UtcDateTime,
                UserId = "u1",
                DisplayName = "Viewer",
                Message = "hello",
            });

            manager.AddMessage(new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "YTChan",
                ChannelId = "UC-999",
                ChatSessionId = "live-secret-id",
                MessageId = "yt-1",
                Timestamp = clock.UtcNowValue.UtcDateTime.AddSeconds(1),
                UserId = "u2",
                DisplayName = "YTViewer",
                Message = "reply",
                IsBot = true,
                CommandResponse = new("ранг", CommandSettings.WhisperToCaller),
            });

            await store.FlushDirtyAsync(manager, CancellationToken.None);
        }

        var files = Directory.GetFiles(_cacheDir);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(files, Has.Length.EqualTo(2));
            Assert.That(files.Select(Path.GetFileName), Is.All.Matches<string>(static name => ChatHistoryCacheStore.IsCacheFileName(name!)));
            Assert.That(Directory.GetFiles(_cacheDir, "*.bak"), Is.Empty);
            Assert.That(files.Any(static f => f.Contains("SecretChannelName", StringComparison.OrdinalIgnoreCase) || f.Contains("live-secret-id", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        using var store2 = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock);
        using var manager2 = CreateManager(settingsPath, chatPath, store2, clock, out var bus2);
        var loadedEvents = new List<ChatHistoryLoaded>();
        using var sub = bus2.Subscribe<ChatHistoryLoaded>(e => loadedEvents.Add(e));
        using var lifecycle = new ChatHistoryCacheLifecycle(manager2, store2, bus2, NullLogger<ChatHistoryCacheLifecycle>.Instance, clock);

        await lifecycle.StartAsync(CancellationToken.None);
        await lifecycle.StopAsync(CancellationToken.None);

        var restored = manager2.GetHistory();
        var ytRestored = restored.Single(static m => m.Platform == StreamingPlatform.YouTube);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedEvents, Has.Count.EqualTo(2));
            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(restored, Is.All.Matches<ChatMessageData>(static m => m.IsHistorical));
            Assert.That(ytRestored.IsBot, Is.True);
            Assert.That(ytRestored.CommandResponse, Is.EqualTo(new CommandResponseMark("ранг", CommandSettings.WhisperToCaller)));
        }
    }

    [Test]
    public async Task Retention30Days_PrunesExpiredBeforePublish_AndOnPeriodicTick()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        using (var store1 = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock))
        using (var manager1 = CreateManager(settingsPath, chatPath, store1, clock, out _))
        {
            manager1.AddMessage(new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "ch",
                ChannelId = "1",
                MessageId = "very-old",
                Timestamp = clock.UtcNowValue.AddDays(-29).UtcDateTime,
                UserId = "u",
                DisplayName = "U",
                Message = "expiring-soon",
            });

            manager1.AddMessage(new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "ch",
                ChannelId = "1",
                MessageId = "fresh",
                Timestamp = clock.UtcNowValue.AddDays(-5).UtcDateTime,
                UserId = "u",
                DisplayName = "U",
                Message = "kept",
            });

            await store1.FlushDirtyAsync(manager1, CancellationToken.None);
        }

        clock.UtcNowValue = clock.UtcNowValue.AddDays(2);

        using var store2 = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock);
        using var manager2 = CreateManager(settingsPath, chatPath, store2, clock, out var bus2);
        var loadedEvents = new List<ChatHistoryLoaded>();
        using var sub = bus2.Subscribe<ChatHistoryLoaded>(e => loadedEvents.Add(e));

        var events = await store2.LoadAndRestoreAsync(manager2, CancellationToken.None);
        foreach (var e in events)
        {
            await bus2.PublishAsync(e);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadedEvents.SelectMany(static e => e.AddedEntries).Select(static m => m.MessageId), Is.EqualTo(new[] { "fresh" }));
            Assert.That(manager2.GetHistory().Select(static m => m.MessageId), Is.EqualTo(new[] { "fresh" }));
        }

        clock.UtcNowValue = clock.UtcNowValue.AddDays(26);
        await store2.PruneExpiredAsync(manager2, CancellationToken.None);

        Assert.That(manager2.GetHistory(), Is.Empty);
    }

    [Test]
    public async Task CorruptFile_IsolatesFailureAndAllowsOtherChannels()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        var corruptScope = ChatHistoryScope.ForTwitch("999", "broken");
        var corruptFileName = corruptScope.ComputeCacheFileName();
        Directory.CreateDirectory(_cacheDir);
        var corruptPath = Path.Combine(_cacheDir, corruptFileName);
        const string damagedBytes = "{ not valid json !!!";
        await File.WriteAllTextAsync(corruptPath, damagedBytes);

        using (var store = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock))
        using (var manager = CreateManager(settingsPath, chatPath, store, clock, out _))
        {
            await store.LoadAndRestoreAsync(manager, CancellationToken.None);

            manager.AddMessage(new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "broken",
                ChannelId = "999",
                MessageId = "b1",
                Timestamp = clock.UtcNowValue.UtcDateTime,
                UserId = "u",
                DisplayName = "U",
                Message = "should-not-overwrite-corrupt",
            });

            manager.AddMessage(new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "healthy",
                ChannelId = "UC1",
                ChatSessionId = "live1",
                MessageId = "h1",
                Timestamp = clock.UtcNowValue.UtcDateTime,
                UserId = "u",
                DisplayName = "U",
                Message = "saved-ok",
            });

            await store.FlushDirtyAsync(manager, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await File.ReadAllTextAsync(corruptPath), Is.EqualTo(damagedBytes));
                Assert.That(Directory.GetFiles(_cacheDir, $"{Path.GetFileNameWithoutExtension(corruptFileName)}.invalid-*"), Has.Length.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(_cacheDir, ChatHistoryScope.ForYouTube("UC1", "live1", "healthy").ComputeCacheFileName())), Is.True);
            }

            var clearResult = manager.ClearHistory();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clearResult.IsComplete, Is.False);
                Assert.That(await File.ReadAllTextAsync(corruptPath), Is.EqualTo(damagedBytes));
            }
        }
    }

    [Test]
    public async Task UnsupportedSchemaVersion_MovesFileToInvalidBackup_AndBlocksOverwrite()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        var futureScope = ChatHistoryScope.ForTwitch("999", "future");
        var fileName = futureScope.ComputeCacheFileName();
        Directory.CreateDirectory(_cacheDir);
        var path = Path.Combine(_cacheDir, fileName);
        const string futureJson = """{"schemaVersion":2,"platform":0,"channelId":"999","messages":[]}""";
        await File.WriteAllTextAsync(path, futureJson);

        using var store = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock);
        using var manager = CreateManager(settingsPath, chatPath, store, clock, out _);

        await store.LoadAndRestoreAsync(manager, CancellationToken.None);

        manager.AddMessage(new()
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "future",
            ChannelId = "999",
            MessageId = "f1",
            Timestamp = clock.UtcNowValue.UtcDateTime,
            UserId = "u",
            DisplayName = "U",
            Message = "should-not-overwrite",
        });

        await store.FlushDirtyAsync(manager, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(futureJson));
            Assert.That(Directory.GetFiles(_cacheDir, $"{Path.GetFileNameWithoutExtension(fileName)}.invalid-*"), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task SizeLimit32MiB_EvictsOldestMessagesAcrossWritableScopes()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var chatPath = Path.Combine(_directory.FullName, "chat.json");

        using var store = new ChatHistoryCacheStore(NullLogger<ChatHistoryCacheStore>.Instance, _cacheDir, clock);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        var chatSettings = new ChatSettingsStore(settingsManager, null, chatPath);
        chatSettings.Save(new() { HistoryMaxItems = 10_000 });
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var manager = new ChatHistoryManager(settingsManager, bus, chatSettings, store, clock);

        var largeText = new string('x', 50_000);
        for (var i = 0; i < 720; i++)
        {
            var scopeChannel = i % 2 == 0 ? "ch-even" : "ch-odd";
            var scopeId = i % 2 == 0 ? "1" : "2";
            manager.AddMessage(new()
            {
                Platform = StreamingPlatform.Twitch,
                Channel = scopeChannel,
                ChannelId = scopeId,
                MessageId = $"m-{i}",
                Timestamp = clock.UtcNowValue.AddSeconds(i).UtcDateTime,
                UserId = "u1",
                DisplayName = "U",
                Message = largeText,
            });
        }

        await store.FlushDirtyAsync(manager, CancellationToken.None);

        var totalBytes = Directory.EnumerateFiles(_cacheDir).Sum(static path => new FileInfo(path).Length);
        var remainingIds = manager.GetHistory().Select(static m => m.MessageId).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totalBytes, Is.LessThanOrEqualTo(ChatHistoryCacheStore.MaxTotalCacheBytes));
            Assert.That(remainingIds, Does.Not.Contain("m-0"));
            Assert.That(remainingIds, Does.Contain("m-719"));
        }
    }

    private ChatHistoryManager CreateManager(
        string settingsPath,
        string chatPath,
        ChatHistoryCacheStore store,
        TimeProvider clock,
        out InMemoryEventBus bus)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        var chatSettings = new ChatSettingsStore(settingsManager, null, chatPath);
        bus = new(NullLogger<InMemoryEventBus>.Instance);
        return new(settingsManager, bus, chatSettings, store, clock);
    }
}
