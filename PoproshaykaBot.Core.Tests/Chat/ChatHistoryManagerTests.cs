using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatHistoryManagerTests
{
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNowValue;
    }

    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("chat-history-mgr");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Scopes_Isolate_Channels_And_YouTube_Live_Chats()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var manager = CreateManager(now, out _);

        var twitchScope = ChatHistoryScope.ForTwitch("100", "chan");
        var otherTwitchScope = ChatHistoryScope.ForTwitch("200", "other");
        var ytBroadcast1 = ChatHistoryScope.ForYouTube("UC100", "live-1", "YouTube");
        var ytBroadcast2 = ChatHistoryScope.ForYouTube("UC100", "live-2", "YouTube");

        manager.AddMessage(LiveMessage(StreamingPlatform.Twitch, "100", "chan", "same-id", now.UtcDateTime, "tw"));
        manager.AddMessage(LiveMessage(StreamingPlatform.Twitch, "200", "other", "same-id", now.UtcDateTime, "other tw"));
        manager.AddMessage(LiveMessage(StreamingPlatform.Twitch, "100", "renamed", "same-id", now.UtcDateTime, "duplicate"));
        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC100", "YouTube", "same-id", now.UtcDateTime.AddSeconds(1), "yt1", "live-1"));
        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC100", "YouTube", "same-id", now.UtcDateTime.AddSeconds(2), "yt2", "live-2"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manager.GetHistory(twitchScope).Select(static m => m.Message), Is.EqualTo(new[] { "tw" }));
            Assert.That(manager.GetHistory(otherTwitchScope).Select(static m => m.Message), Is.EqualTo(new[] { "other tw" }));
            Assert.That(manager.GetHistory(ytBroadcast1).Select(static m => m.Message), Is.EqualTo(new[] { "yt1" }));
            Assert.That(manager.GetHistory(ytBroadcast2).Select(static m => m.Message), Is.EqualTo(new[] { "yt2" }));
            Assert.That(manager.GetHistory(), Has.Count.EqualTo(4));
        }
    }

    [Test]
    public void LateHistoryImport_PreservesLiveMessages_AndDeduplicatesByMessageId()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var manager = CreateManager(now, out _);
        var scope = ChatHistoryScope.ForYouTube("UC1", "chat-1", "Chan");

        var liveEntry = LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m-live", now.UtcDateTime, "live-msg", "chat-1");
        manager.AddMessage(liveEntry);

        var candidates = new[]
        {
            LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m-old", now.UtcDateTime.AddMinutes(-2), "hist-1", "chat-1"),
            LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m-live", now.UtcDateTime, "duplicate-live", "chat-1"),
        };

        var outcome = manager.ImportHistory(scope, candidates);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.AddedEntries.Select(static m => m.MessageId), Is.EqualTo(new[] { "m-old" }));
            Assert.That(outcome.MergedHistory.Select(static m => m.Message), Is.EqualTo(new[] { "hist-1", "live-msg" }));
            Assert.That(outcome.MergedHistory[0].IsHistorical, Is.True);
            Assert.That(outcome.MergedHistory[1].IsHistorical, Is.False);
        }
    }

    [Test]
    public void ClearHistory_RejectsOlderLateSnapshots_WhileAcceptingNewLiveMessages()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var manager = CreateManager(clock.UtcNowValue, out _, clock);
        var scope = ChatHistoryScope.ForYouTube("UC1", "chat-1", "Chan");

        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m1", clock.UtcNowValue.UtcDateTime.AddSeconds(-5), "before", "chat-1"));
        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(10);

        manager.ClearHistory();

        var lateOutcome = manager.ImportHistory(scope,
        [
            LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m-stale", clock.UtcNowValue.UtcDateTime.AddSeconds(-2), "stale", "chat-1"),
        ]);

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(5);
        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "m-new", clock.UtcNowValue.UtcDateTime, "after-clear", "chat-1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lateOutcome.AddedEntries, Is.Empty);
            Assert.That(manager.GetHistory(scope).Select(static m => m.Message), Is.EqualTo(new[] { "after-clear" }));
        }
    }

    [Test]
    public void ModerationTombstones_DeleteOnlyTargetScope_AndBlockReimport()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var manager = CreateManager(now, out _);

        var ytScope = ChatHistoryScope.ForYouTube("UC1", "chat-1", "Chan");
        var twScope = ChatHistoryScope.ForTwitch("tw-1", "twchan");

        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "yt-1", now.UtcDateTime.AddSeconds(-10), "keep", "chat-1", userId: "u-good"));
        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "yt-2", now.UtcDateTime.AddSeconds(-5), "del-msg", "chat-1", userId: "u-good"));
        manager.AddMessage(LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "yt-3", now.UtcDateTime.AddSeconds(-3), "banned", "chat-1", userId: "u-bad"));
        manager.AddMessage(LiveMessage(StreamingPlatform.Twitch, "tw-1", "twchan", "yt-2", now.UtcDateTime.AddSeconds(-5), "tw-untouched", userId: "u-bad"));

        var removedSingle = manager.ApplyModeration(ytScope, new(ChatModerationActionKind.MessageDeleted, "yt-2", null, now));
        var removedAuthor = manager.ApplyModeration(ytScope, new(ChatModerationActionKind.AuthorMessagesDeleted, null, "u-bad", now));

        var reimport = manager.ImportHistory(ytScope,
        [
            LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "yt-2", now.UtcDateTime.AddSeconds(-5), "del-msg", "chat-1", userId: "u-good"),
            LiveMessage(StreamingPlatform.YouTube, "UC1", "Chan", "yt-3", now.UtcDateTime.AddSeconds(-3), "banned", "chat-1", userId: "u-bad"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(removedSingle, Is.EqualTo(new[] { "yt-2" }));
            Assert.That(removedAuthor, Is.EqualTo(new[] { "yt-3" }));
            Assert.That(reimport.AddedEntries, Is.Empty);
            Assert.That(manager.GetHistory(ytScope).Select(static m => m.MessageId), Is.EqualTo(new[] { "yt-1" }));
            Assert.That(manager.GetHistory(twScope).Select(static m => m.MessageId), Is.EqualTo(new[] { "yt-2" }));
        }
    }

    [Test]
    public void PreservesBotEchoFlagAndCommandResponseMark_AcrossBufferSnapshot()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var manager = CreateManager(now, out _);
        var scope = ChatHistoryScope.ForTwitch("1", "chan");

        var msg = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "chan",
            ChannelId = "1",
            MessageId = "bot-1",
            Timestamp = now.UtcDateTime,
            UserId = "bot",
            DisplayName = "Bot",
            Message = "whispered",
            MessageType = ChatMessageType.BotResponse,
            IsBot = true,
            CommandResponse = new("ранг", CommandSettings.WhisperToCaller),
        };

        manager.AddMessage(msg);
        var saved = manager.GetHistory(scope).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.IsBot, Is.True);
            Assert.That(saved.CommandResponse, Is.EqualTo(new CommandResponseMark("ранг", CommandSettings.WhisperToCaller)));
        }
    }

    private ChatHistoryManager CreateManager(DateTimeOffset now, out ChatSettingsStore chatSettings, TimeProvider? timeProvider = null)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));
        chatSettings = new(settingsManager, null, Path.Combine(_directory.FullName, "chat.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        return new(settingsManager, bus, chatSettings, null, timeProvider ?? new FakeTimeProvider(now));
    }

    private static ChatMessageData LiveMessage(
        StreamingPlatform platform,
        string channelId,
        string channel,
        string id,
        DateTime timestampUtc,
        string text,
        string? sessionId = null,
        string userId = "u1")
    {
        return new()
        {
            Platform = platform,
            Channel = channel,
            ChannelId = channelId,
            ChatSessionId = sessionId,
            MessageId = id,
            Timestamp = timestampUtc,
            UserId = userId,
            DisplayName = "User",
            Message = text,
        };
    }
}
