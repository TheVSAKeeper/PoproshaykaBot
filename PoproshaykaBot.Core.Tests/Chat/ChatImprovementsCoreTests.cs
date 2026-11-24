using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatImprovementsCoreTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pb-chat-imp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Test]
    public void TwitchIrcMessageParser_ParsesPrivMsgTagsBadgesEmotesAndAction()
    {
        var line = "@badge-info=subscriber/12;badges=moderator/1,subscriber/12;color=#1E90FF;display-name=Alice\\sOne;emotes=25:0-4;id=msg-123;mod=1;room-id=177128531;subscriber=1;tmi-sent-ts=1763913600000;user-id=999 :alice!alice@alice.tmi.twitch.tv PRIVMSG #bobito217 :\u0001ACTION Kappa hello\u0001";

        var ok = TwitchIrcMessageParser.TryParsePrivMsg(line, "fallback", "177128531", "999", out var msg, out var ts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(msg.Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(msg.Channel, Is.EqualTo("bobito217"));
            Assert.That(msg.ChannelId, Is.EqualTo("177128531"));
            Assert.That(msg.Id, Is.EqualTo("msg-123"));
            Assert.That(msg.UserId, Is.EqualTo("999"));
            Assert.That(msg.Username, Is.EqualTo("alice"));
            Assert.That(msg.DisplayName, Is.EqualTo("Alice One"));
            Assert.That(msg.Message, Is.EqualTo("Kappa hello"));
            Assert.That(msg.Color, Is.EqualTo("#1E90FF"));
            Assert.That(msg.IsModerator, Is.True);
            Assert.That(msg.IsSubscriber, Is.True);
            Assert.That(msg.IsBot, Is.True);
            Assert.That(msg.Badges, Has.Count.EqualTo(2));
            Assert.That(msg.Emotes, Has.Count.EqualTo(1));
            Assert.That(msg.Emotes[0].EmoteId, Is.EqualTo("25"));
            Assert.That(msg.Emotes[0].Name, Is.EqualTo("Kappa"));
            Assert.That(ts, Is.EqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1763913600000)));
        }
    }

    [Test]
    public void TwitchIrcMessageParser_IgnoresNonPrivMsgAndMissingTimestamp()
    {
        var userNotice = "@id=u1;tmi-sent-ts=1763913600000 :tmi.twitch.tv USERNOTICE #bobito217 :resub";
        var missingTs = "@id=m1 :alice!alice@alice.tmi.twitch.tv PRIVMSG #bobito217 :hi";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TwitchIrcMessageParser.TryParsePrivMsg(userNotice, "bobito217", "1", null, out _, out _), Is.False);
            Assert.That(TwitchIrcMessageParser.TryParsePrivMsg(missingTs, "bobito217", "1", null, out _, out _), Is.False);
        }
    }

    [Test]
    public async Task TwitchRecentHistoryLoader_LoadsHistoryOnlyWhenEnabled_AndImportsIntoHistoryManager()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var chatSettings = new ChatSettingsStore(null, null, Path.Combine(_tempDir, "chat.json"));
        var obsChat = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_tempDir, "obs.json"));
        var channels = new PlatformChannelRegistry();
        channels.SetBinding(new ActiveChannelBinding(new ChannelKey(StreamingPlatform.Twitch, "177128531"), "bobito217", "default", "default", 1));
        var targetProvider = Substitute.For<ITargetChannelProvider>();
        targetProvider.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));
        var botIdProvider = Substitute.For<IBotUserIdProvider>();
        botIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("bot-1");

        var twitchPlatform = Substitute.For<IChatPlatform>();
        twitchPlatform.Id.Returns(StreamingPlatform.Twitch);
        twitchPlatform.Capabilities.Returns(PlatformCapabilities.Reply | PlatformCapabilities.Whisper);
        twitchPlatform.Decorations.Returns(NullChatDecorations.Instance);
        var twitchStatus = Substitute.For<IStreamStatus>();
        twitchStatus.CurrentStatus.Returns(StreamStatus.Offline);
        twitchPlatform.StreamStatus.Returns(twitchStatus);

        var registry = new ChatPlatformRegistry([twitchPlatform]);
        using var historyManager = new ChatHistoryManager(settingsManager, bus, chatSettings);
        using var batchHandler = new ChatHistoryBatchHandler(registry, obsChat, historyManager, bus);

        var loadedTcs = new TaskCompletionSource<ChatHistoryLoaded>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loadedSub = bus.Subscribe<ChatHistoryLoaded>(e => loadedTcs.TrySetResult(e));

        var sampleMsg = new ChatMessage(
            StreamingPlatform.Twitch,
            "bobito217",
            "recent-1",
            "u1",
            "alice",
            "Alice",
            "Привет из recent-messages",
            [],
            [],
            false,
            false,
            false,
            false,
            false,
            "",
            "177128531");
        var recentClient = Substitute.For<ITwitchRecentMessagesClient>();
        recentClient.FetchAsync("bobito217", "177128531", "bot-1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(sampleMsg, DateTimeOffset.UtcNow.AddMinutes(-2))]);

        using var loader = new TwitchRecentHistoryLoader(
            recentClient,
            settingsManager,
            chatSettings,
            channels,
            targetProvider,
            botIdProvider,
            bus,
            NullLogger<TwitchRecentHistoryLoader>.Instance);

        await bus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, DateTimeOffset.UtcNow, "177128531"));
        await recentClient.DidNotReceiveWithAnyArgs().FetchAsync(default!, default!, default, default, default);

        settingsManager.Mutate(static s => s.Twitch.FetchRecentMessages = true);
        chatSettings.Save(new() { ShowHistoryInApp = true });
        await bus.PublishAsync(new ChatIngestionStarted(StreamingPlatform.Twitch, DateTimeOffset.UtcNow, "177128531"));

        var loaded = await loadedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.AddedEntries, Has.Count.EqualTo(1));
            Assert.That(loaded.AddedEntries[0].MessageId, Is.EqualTo("recent-1"));
            Assert.That(loaded.AddedEntries[0].IsHistorical, Is.True);
            Assert.That(historyManager.GetActiveHistory().Select(static m => m.MessageId), Contains.Item("recent-1"));
        }
    }

    [Test]
    public async Task ChatHistoryManager_YouTubeActiveBroadcastChanged_SwitchesActiveScopeAwayFromOldStream()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-yt.json"));
        using var historyManager = new ChatHistoryManager(settingsManager, bus);

        var oldScope = ChatHistoryScope.ForYouTube("UC-1", "live-yesterday", "Chan");
        historyManager.ImportHistory(oldScope,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "Chan",
                ChannelId = "UC-1",
                ChatSessionId = "live-yesterday",
                MessageId = "yt-old",
                Timestamp = DateTime.UtcNow.AddDays(-1),
                UserId = "u1",
                DisplayName = "OldViewer",
                Message = "Yesterday",
            },
        ]);

        Assert.That(historyManager.GetActiveHistory(), Has.Count.EqualTo(1));

        await bus.PublishAsync(new YouTubeActiveBroadcastChanged(new YouTubeBroadcastSummary(
            "video-today",
            "UC-1",
            "Chan",
            "live-today",
            "Today stream",
            DateTimeOffset.UtcNow)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(historyManager.GetActiveHistory(), Is.Empty);
            Assert.That(historyManager.GetActiveChannelHistory().Select(static m => m.MessageId), Is.EqualTo(new[] { "yt-old" }));
        }
    }

    [Test]
    public void ChatHistoryBuffer_PersistsAndRestoresStreamMetadata()
    {
        var scope = ChatHistoryScope.ForTwitch("177128531", "bobito217");
        var buffer = new ChatHistoryBuffer(scope);
        var broadcastId = Guid.NewGuid();
        var startedAt = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);
        var now = new DateTimeOffset(2026, 11, 23, 15, 30, 0, TimeSpan.Zero);

        buffer.TryAddLive(new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "bobito217",
            ChannelId = "177128531",
            BroadcastId = broadcastId,
            StreamId = "stream-42",
            StreamStartedAtUtc = startedAt,
            StreamTitle = "Таркуем",
            MessageId = "m-stream",
            Timestamp = now.UtcDateTime,
            UserId = "u1",
            DisplayName = "User1",
            Message = "hello",
        }, now - TimeSpan.FromDays(30), 100);

        var doc = buffer.ToDocument();
        var restoredBuffer = new ChatHistoryBuffer(scope);
        var restored = restoredBuffer.RestoreFromDocument(doc, now - TimeSpan.FromDays(30), 100, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored, Has.Count.EqualTo(1));
            Assert.That(restored[0].BroadcastId, Is.EqualTo(broadcastId));
            Assert.That(restored[0].StreamId, Is.EqualTo("stream-42"));
            Assert.That(restored[0].StreamStartedAtUtc, Is.EqualTo(startedAt));
            Assert.That(restored[0].StreamTitle, Is.EqualTo("Таркуем"));
        }
    }

    [Test]
    public void YouTubeAccountsStore_SetBrowserProfile_SyncsAcrossRolesAndPersists()
    {
        var path = Path.Combine(_tempDir, "youtube-accounts.json");
        var store = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, path);
        store.UpsertContexts(
        [
            (PlatformAuthRole.Broadcaster, "yt-broadcaster-UC1", "MyYT", new YouTubeAccountSettings
            {
                AccessToken = "tok-b",
                ChannelId = "UC1",
                ChannelTitle = "MyYT",
            }),
            (PlatformAuthRole.Bot, "yt-bot-UC1", "MyYT", new YouTubeAccountSettings
            {
                AccessToken = "tok-bot",
                ChannelId = "UC1",
                ChannelTitle = "MyYT",
            }),
        ]);

        var profileId = Guid.NewGuid().ToString("N");
        var written = store.SetBrowserProfile("UC1", profileId);

        var reloaded = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, path);
        var accounts = reloaded.ListAccounts();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written, Is.True);
            Assert.That(accounts, Has.Count.EqualTo(1));
            Assert.That(accounts[0].Authorizations.All(a => a.ChatBrowserProfile?.ProfileName == profileId), Is.True);
        }

        reloaded.SetBrowserProfile("UC1", null);
        var afterReset = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, path).ListAccounts();
        Assert.That(afterReset[0].Authorizations.All(a => a.ChatBrowserProfile is null), Is.True);
    }

    [Test]
    public void AccountsStore_SetChatBrowserProfile_SyncsAcrossRolesAndPersists()
    {
        var path = Path.Combine(_tempDir, "twitch-accounts.json");
        var store = new AccountsStore(NullLogger<AccountsStore>.Instance, path);
        store.UpsertContexts(
        [
            (PlatformAuthRole.Broadcaster, "default", "streamer1", new TwitchAccountSettings
            {
                AccessToken = "tok-b",
                UserId = "1001",
                Login = "streamer1",
            }),
            (PlatformAuthRole.Bot, "tw-bot-1001", "streamer1", new TwitchAccountSettings
            {
                AccessToken = "tok-bot",
                UserId = "1001",
                Login = "streamer1",
            }),
        ]);

        var profileId = Guid.NewGuid().ToString("N");
        IPlatformAccountDirectory directory = store;
        var written = directory.SetChatBrowserProfile("1001", profileId);

        var reloaded = new AccountsStore(NullLogger<AccountsStore>.Instance, path);
        var accounts = reloaded.ListAccounts();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(directory.SupportsChatBrowserProfile, Is.True);
            Assert.That(written, Is.True);
            Assert.That(accounts, Has.Count.EqualTo(1));
            Assert.That(accounts[0].Authorizations.All(a => a.ChatBrowserProfile?.ProfileName == profileId), Is.True);
        }

        reloaded.SetChatBrowserProfile("1001", null);
        var afterReset = new AccountsStore(NullLogger<AccountsStore>.Instance, path).ListAccounts();
        Assert.That(afterReset[0].Authorizations.All(a => a.ChatBrowserProfile is null), Is.True);
    }

    [Test]
    public void YouTubeAccountsStore_ReplacingAccountWithDifferentChannel_DoesNotInheritBrowserProfile()
    {
        var path = Path.Combine(_tempDir, "youtube-accounts-swap.json");
        var store = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, path);
        var profileA = Guid.NewGuid().ToString("N");
        store.UpsertContexts(
        [
            (PlatformAuthRole.Broadcaster, "default", "ChannelA", new YouTubeAccountSettings
            {
                AccessToken = "tok-a",
                ChannelId = "UC-A",
                ChannelTitle = "ChannelA",
                BrowserProfileName = profileA,
            }),
        ]);

        store.UpsertContexts(
        [
            (PlatformAuthRole.Broadcaster, "default", "ChannelB", new YouTubeAccountSettings
            {
                AccessToken = "tok-b",
                ChannelId = "UC-B",
                ChannelTitle = "ChannelB",
                BrowserProfileName = null,
            }),
        ]);

        var reloaded = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, path);
        var accountB = reloaded.ListAccounts().Single(a => a.Key.AccountId == "UC-B");
        Assert.That(accountB.Authorizations.All(a => a.ChatBrowserProfile is null), Is.True);
    }

    [Test]
    public void TwitchIrcMessageParser_SkipsDeletedBadTimestampMismatchedRoom_AndMapsCodePointEmotesToUtf16()
    {
        var deletedLine = "@id=m-del;rm-deleted=1;room-id=177128531;tmi-sent-ts=1763913600000 :alice!alice@tmi.twitch.tv PRIVMSG #bobito217 :removed";
        var overflowTs = "@id=m-ts;room-id=177128531;tmi-sent-ts=999999999999999999 :alice!alice@tmi.twitch.tv PRIVMSG #bobito217 :overflow";
        var wrongRoom = "@id=m-room;room-id=999999;tmi-sent-ts=1763913600000 :alice!alice@tmi.twitch.tv PRIVMSG #bobito217 :other";
        var emojiEmote = "@id=m-emoji;emotes=25:2-6;room-id=177128531;tmi-sent-ts=1763913600000 :alice!alice@tmi.twitch.tv PRIVMSG #bobito217 :😀 Kappa";

        var okEmoji = TwitchIrcMessageParser.TryParsePrivMsg(emojiEmote, "bobito217", "177128531", null, out var parsed, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TwitchIrcMessageParser.TryParsePrivMsg(deletedLine, "bobito217", "177128531", null, out _, out _), Is.False);
            Assert.That(TwitchIrcMessageParser.TryParsePrivMsg(overflowTs, "bobito217", "177128531", null, out _, out _), Is.False);
            Assert.That(TwitchIrcMessageParser.TryParsePrivMsg(wrongRoom, "bobito217", "177128531", null, out _, out _), Is.False);
            Assert.That(okEmoji, Is.True);
            Assert.That(parsed.Emotes, Has.Count.EqualTo(1));
            Assert.That(parsed.Emotes[0].Name, Is.EqualTo("Kappa"));
            Assert.That(parsed.Emotes[0].StartIndex, Is.EqualTo(3));
            Assert.That(parsed.Emotes[0].EndIndex, Is.EqualTo(7));
        }
    }

    [Test]
    public async Task ChatHistoryManager_PinnedYouTubeBroadcast_IsNotOverwrittenByLateOldLiveChatActivity()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-pin.json"));
        using var historyManager = new ChatHistoryManager(settingsManager, bus);

        await bus.PublishAsync(new YouTubeActiveBroadcastChanged(new YouTubeBroadcastSummary(
            "video-new",
            "UC-1",
            "Chan",
            "live-new",
            "New stream",
            DateTimeOffset.UtcNow)));

        var oldScope = ChatHistoryScope.ForYouTube("UC-1", "live-old", "Chan");
        historyManager.ImportHistory(oldScope,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "Chan",
                ChannelId = "UC-1",
                ChatSessionId = "live-old",
                MessageId = "yt-late-old",
                Timestamp = DateTime.UtcNow,
                UserId = "u1",
                DisplayName = "OldViewer",
                Message = "Late old message",
            },
        ]);

        Assert.That(historyManager.GetActiveHistory(), Is.Empty);
    }

    [Test]
    public async Task ChatHistoryBatchHandler_MatchesCommandResponseWithChannelKey()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-cmd.json"));
        var obsChat = new ObsChatStore(bus, NullLogger<ObsChatStore>.Instance, Path.Combine(_tempDir, "obs-cmd.json"));
        var tracker = new CommandResponseTracker(TimeProvider.System);
        var channelKey = new ChannelKey(StreamingPlatform.Twitch, "177128531");
        tracker.Expect(StreamingPlatform.Twitch, "bot-msg-1", new CommandResponseMark("!ping", Core.Chat.Commands.CommandResponseTarget.Chat), channelKey);

        var twitchPlatform = Substitute.For<IChatPlatform>();
        twitchPlatform.Id.Returns(StreamingPlatform.Twitch);
        twitchPlatform.Capabilities.Returns(PlatformCapabilities.Reply);
        twitchPlatform.Decorations.Returns(NullChatDecorations.Instance);
        var status = Substitute.For<IStreamStatus>();
        status.CurrentStatus.Returns(StreamStatus.Offline);
        twitchPlatform.StreamStatus.Returns(status);

        var registry = new ChatPlatformRegistry([twitchPlatform]);
        using var historyManager = new ChatHistoryManager(settingsManager, bus);
        using var batchHandler = new ChatHistoryBatchHandler(registry, obsChat, historyManager, bus, tracker);

        var botMsg = new ChatMessage(
            StreamingPlatform.Twitch,
            "bobito217",
            "bot-msg-1",
            "bot-1",
            "bot",
            "Bot",
            "pong",
            [],
            [],
            false,
            false,
            false,
            false,
            true,
            "",
            "177128531");

        await bus.PublishAsync(new RawChatHistoryReceived(
            ChatHistoryScope.ForTwitch("177128531", "bobito217"),
            [(botMsg, DateTimeOffset.UtcNow)],
            AllowExternalHistory: true));

        var entry = historyManager.GetActiveHistory().Single();
        Assert.That(entry.CommandResponse?.Target, Is.EqualTo(Core.Chat.Commands.CommandResponseTarget.Chat));
    }

    [Test]
    public void ObsOverlayMessageFilter_RejectsFinishedStreamOffline_AndDistinguishesTwitchStreamIdInMultistream()
    {
        var sharedBroadcastId = Guid.NewGuid();
        var twitchStatus = Substitute.For<IStreamStatus>();
        twitchStatus.CurrentStatus.Returns(StreamStatus.Offline);
        var twitchPlatform = Substitute.For<IChatPlatform>();
        twitchPlatform.Id.Returns(StreamingPlatform.Twitch);
        twitchPlatform.StreamStatus.Returns(twitchStatus);
        var registry = new ChatPlatformRegistry([twitchPlatform]);
        var broadcastState = Substitute.For<Core.Broadcast.IBroadcastState>();
        broadcastState.CurrentBroadcastId.Returns(sharedBroadcastId);

        var finishedLiveMsg = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "177128531",
            BroadcastId = sharedBroadcastId,
            StreamId = "twitch-stream-1",
            StreamStartedAtUtc = DateTime.UtcNow.AddHours(-1),
            MessageId = "m-finished",
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
            IsHistorical = false,
            Message = "from ended stream",
        };
        var offlineTestMsg = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "177128531",
            MessageId = "m-offline-test",
            Timestamp = DateTime.UtcNow,
            IsHistorical = false,
            Message = "offline test",
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Core.Server.ObsOverlayMessageFilter.AllowsCurrentStream(finishedLiveMsg, registry, null, broadcastState), Is.False);
            Assert.That(Core.Server.ObsOverlayMessageFilter.AllowsCurrentStream(offlineTestMsg, registry, null, broadcastState), Is.True);
        }

        twitchStatus.CurrentStatus.Returns(StreamStatus.Online);
        twitchStatus.CurrentStream.Returns(new StreamInfo { Id = "twitch-stream-2", StartedAt = DateTime.UtcNow.AddMinutes(-2) });

        var newStreamMsg = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "177128531",
            BroadcastId = sharedBroadcastId,
            StreamId = "twitch-stream-2",
            StreamStartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            MessageId = "m-stream2",
            Timestamp = DateTime.UtcNow,
            IsHistorical = false,
            Message = "new stream",
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Core.Server.ObsOverlayMessageFilter.AllowsCurrentStream(finishedLiveMsg, registry, null, broadcastState), Is.False);
            Assert.That(Core.Server.ObsOverlayMessageFilter.AllowsCurrentStream(newStreamMsg, registry, null, broadcastState), Is.True);
        }
    }
}
