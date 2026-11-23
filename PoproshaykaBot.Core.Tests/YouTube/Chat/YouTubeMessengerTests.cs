using Microsoft.Extensions.Logging;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Collections.Concurrent;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeMessengerTests
{
    private string _directory = null!;
    private ManualTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;
    private YouTubeSentMessageTracker _sentTracker = null!;
    private CommandResponseTracker _responseTracker = null!;
    private IYouTubeActiveBroadcastSource _broadcastSource = null!;
    private ITargetChannelProvider _targetProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-messenger-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));
        _settingsStore = new YouTubeSettingsStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj-1", DailyQuotaBudget = 10_000 });

        _quotaStore = new YouTubeQuotaStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeQuotaGovernor>.Instance);

        _sentTracker = new YouTubeSentMessageTracker();
        _responseTracker = new CommandResponseTracker(_clock);

        _broadcastSource = Substitute.For<IYouTubeActiveBroadcastSource>();
        _broadcastSource.CurrentBroadcast.Returns(new YouTubeBroadcastSummary("vid-1", "UCbroadcaster", "Channel", "chat-1", "Live", _clock.GetUtcNow()));

        _targetProvider = Substitute.For<ITargetChannelProvider>();
        _targetProvider.Current.Returns(new TargetChannelState("UCbroadcaster", "UCbroadcaster", IsDebugSession: false, IsSendingAllowed: true));
    }

    [TearDown]
    public void TearDown()
    {
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Splitter_Preserves_Word_Boundaries_And_Does_Not_Split_Surrogate_Pairs()
    {
        var prefix = new string('a', 199);
        var withEmoji = prefix + "😀" + "tail";

        var chunks = YouTubeMessageSplitter.Split(withEmoji, 200);

        Assert.Multiple(() =>
        {
            Assert.That(chunks, Has.Count.EqualTo(2));
            Assert.That(chunks[0], Is.EqualTo(prefix));
            Assert.That(chunks[0].Length, Is.LessThanOrEqualTo(200));
            Assert.That(chunks[1], Is.EqualTo("😀tail"));
            Assert.That(char.IsHighSurrogate(chunks[0][^1]), Is.False);
            Assert.That(char.IsLowSurrogate(chunks[1][0]), Is.False);
        });
    }

    [Test]
    public async Task Manual_send_uses_selected_account_and_rejects_stale_stream_or_account()
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "selected", "Selected", new()
        {
            ChannelId = "UCselected", AccessToken = "fixture-token", StoredScopes = [YouTubeScopes.ForceSsl],
        });
        var requested = new TaskCompletionSource<(string? ContextId, string? AccountId)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<YouTubeInsertMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubSendClient((_, _, _, ct) =>
        {
            requested.TrySetResult((PlatformAuthRequestOptions.CurrentContextId, PlatformAuthRequestOptions.ExpectedAccountId));
            return response.Task.WaitAsync(ct);
        });
        using var messenger = CreateMessenger(client, accounts);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);
        var sender = new ChatSenderAccount("UCselected", PlatformAuthRole.Bot, "selected");
        Assert.That(await messenger.SendAsAsync(sender, "old-video", "fixture"),
            Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.NotConnected)));
        Assert.That(await messenger.SendAsAsync(sender with { AccountId = "UCanother" }, "vid-1", "fixture"),
            Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.AuthorizationRequired)));

        var send = messenger.SendAsAsync(sender, "vid-1", "fixture");
        Assert.That(await requested.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(("selected", "UCselected")));
        Assert.That(send.IsCompleted, Is.False);
        response.SetResult(new YouTubeInsertMessageResult("sent-id", null));
        Assert.That(await send.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.Queued, ChatDeliveryStatus.Sent, 1)));
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);
    }

    [TestCase(YouTubeApiErrorKind.Unauthorized, ChatDeliveryStatus.AuthorizationRequired)]
    [TestCase(YouTubeApiErrorKind.Forbidden, ChatDeliveryStatus.Rejected)]
    [TestCase(YouTubeApiErrorKind.Transient, ChatDeliveryStatus.Unconfirmed)]
    public async Task Manual_send_reports_actual_api_failure(YouTubeApiErrorKind apiError, ChatDeliveryStatus expected)
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "selected", "Selected", new()
        {
            ChannelId = "UCselected", AccessToken = "fixture-token", StoredScopes = [YouTubeScopes.ForceSsl],
        });
        using var messenger = CreateMessenger(new StubSendClient((_, _, _, _) => throw new YouTubeApiException(apiError, "fixture")), accounts);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var send = messenger.SendAsAsync(new("UCselected", PlatformAuthRole.Bot, "selected"), "vid-1", "fixture");
        await WaitAndAdvanceUntilAsync(() => send.IsCompleted);

        Assert.That(await send, Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.Queued, expected)));
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);
    }

    [Test]
    public async Task Closing_queue_resolves_manual_sends_waiting_for_delivery()
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "youtube-accounts.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "selected", "Selected", new()
        {
            ChannelId = "UCselected", AccessToken = "fixture-token", StoredScopes = [YouTubeScopes.ForceSsl],
        });
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<YouTubeInsertMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var messenger = CreateMessenger(new StubSendClient((_, _, _, ct) =>
        {
            requested.TrySetResult();
            return response.Task.WaitAsync(ct);
        }), accounts);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var account = new ChatSenderAccount("UCselected", PlatformAuthRole.Bot, "selected");
        var first = messenger.SendAsAsync(account, "vid-1", "in flight");
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = messenger.SendAsAsync(account, "vid-1", "queued");
        messenger.Dispose();

        Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.Queued, ChatDeliveryStatus.Unconfirmed)));
        Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)),
            Is.EqualTo(new ChatAccountSendResult(ChatEnqueueResult.Queued, ChatDeliveryStatus.QueueClosed)));
    }

    [Test]
    public async Task MultiChunk_Send_Registers_All_Chunks_And_Drops_Tail_When_Second_Chunk_Hits_Quota()
    {
        var sentCalls = new ConcurrentQueue<(string Text, YouTubeQuotaPriority Priority)>();
        var firstSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendClient = new StubSendClient((chatId, text, priority, _) =>
        {
            sentCalls.Enqueue((text, priority));

            if (sentCalls.Count == 1)
            {
                firstSent.TrySetResult();
                return Task.FromResult(new YouTubeInsertMessageResult("yt-out-1", _clock.GetUtcNow()));
            }

            if (priority == YouTubeQuotaPriority.ScheduledBroadcast && sentCalls.Count == 2)
            {
                throw new YouTubeApiException(YouTubeApiErrorKind.LocalQuotaBudgetReached, "лимит");
            }

            thirdSent.TrySetResult();
            return Task.FromResult(new YouTubeInsertMessageResult("yt-out-cmd", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(sendClient);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var longBroadcast = new string('x', 190) + " " + new string('y', 190);
        var mark = new CommandResponseMark("стрим", CommandResponseTarget.Chat);

        var r1 = messenger.Send(longBroadcast, category: ChatSendCategory.ScheduledBroadcast);
        await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var r2 = messenger.Send("Ответ команды", mark, ChatSendCategory.CommandReply);
        await WaitAndAdvanceUntilAsync(() => thirdSent.Task.IsCompleted);

        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(r1, Is.EqualTo(ChatEnqueueResult.Queued));
            Assert.That(r2, Is.EqualTo(ChatEnqueueResult.Queued));
            Assert.That(_sentTracker.Contains("yt-out-1", "chat-1"), Is.True);
            Assert.That(_sentTracker.Contains("yt-out-cmd", "chat-1"), Is.True);
            Assert.That(_sentTracker.Contains("yt-out-cmd", "chat-2"), Is.False);
            Assert.That(_responseTracker.TryConsume(StreamingPlatform.YouTube, "yt-out-cmd", new(StreamingPlatform.YouTube, "UCbroadcaster"), "chat-2"), Is.Null);
            Assert.That(_responseTracker.TryConsume(StreamingPlatform.YouTube, "yt-out-cmd", new(StreamingPlatform.YouTube, "UCbroadcaster"), "chat-1"), Is.EqualTo(mark));
            Assert.That(sentCalls.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Reply_Avoids_Duplicate_Mention_And_Whisper_Falls_Back_To_Mention()
    {
        var texts = new ConcurrentQueue<string>();
        var allSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendClient = new StubSendClient((_, text, _, _) =>
        {
            texts.Enqueue(text);

            if (texts.Count == 3)
            {
                allSent.TrySetResult();
            }

            return Task.FromResult(new YouTubeInsertMessageResult($"id-{texts.Count}", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(sendClient);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        messenger.Reply("m-1", "Alice", "Добро пожаловать, Alice!", category: ChatSendCategory.Greeting);
        await WaitAndAdvanceUntilAsync(() => texts.Count >= 1);
        messenger.Reply("m-2", "Bob", "Рады видеть на стриме!", category: ChatSendCategory.Greeting);
        await WaitAndAdvanceUntilAsync(() => texts.Count >= 2);
        messenger.Whisper("u-3", "m-3", "Carol", "Твой ранг: Новичок", new CommandResponseMark("ранг", CommandResponseTarget.Whisper), ChatSendCategory.CommandReply);

        await WaitAndAdvanceUntilAsync(() => allSent.Task.IsCompleted);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(texts.ToArray(), Is.EqualTo(new[]
        {
            "Добро пожаловать, Alice!",
            "@Bob Рады видеть на стриме!",
            "@Carol Твой ранг: Новичок",
        }));
    }

    [Test]
    public async Task Transient_Does_Not_Retry_While_401_Retries_Once_And_Observer_Refuses_Synchronously()
    {
        var calls = 0;
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var sendClient = new StubSendClient((_, text, _, _) =>
        {
            calls++;

            if (text == "uncertain")
            {
                throw new YouTubeApiException(YouTubeApiErrorKind.Transient, "network timeout");
            }

            if (calls == 2)
            {
                throw new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401");
            }

            secondCompleted.TrySetResult();
            return Task.FromResult(new YouTubeInsertMessageResult("id-after-401", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(sendClient);

        _targetProvider.Current.Returns(new TargetChannelState("UC", "UC", IsDebugSession: false, IsSendingAllowed: false));
        Assert.That(messenger.Send("blocked"), Is.EqualTo(ChatEnqueueResult.ObserverMode));

        _targetProvider.Current.Returns(new TargetChannelState("UC", "UC", IsDebugSession: false, IsSendingAllowed: true));
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(messenger.Send("uncertain", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));
        Assert.That(messenger.Send("retry401", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));

        await WaitAndAdvanceUntilAsync(() => secondCompleted.Task.IsCompleted);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(_sentTracker.Contains("id-after-401", "chat-1"), Is.True);
            Assert.That(messenger.SentCount, Is.EqualTo(1));
            Assert.That(messenger.FailedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Expiration_PacificRollover_And_LiveChatChange_Drop_Stale_Items_Before_Send()
    {
        var calls = 0;
        var firstSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendClient = new StubSendClient((_, _, _, _) =>
        {
            calls++;
            firstSent.TrySetResult();
            return Task.FromResult(new YouTubeInsertMessageResult($"msg-{calls}", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(sendClient);

        // 23:59:30 PST (07:59:30 UTC on Nov 16) -> 40s later crosses Pacific midnight (08:00:00 UTC)
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 16, 7, 59, 30, TimeSpan.Zero));
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(messenger.Send("first", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));
        await firstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(messenger.Send("midnight-stale", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));

        // Advance past Pacific midnight before SendInterval finishes for the 2nd item
        _clock.Advance(TimeSpan.FromSeconds(45));
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);
        Assert.That(calls, Is.EqualTo(1), "Второй элемент должен быть отброшен при переходе через полночь PT");

        // Now test TTL expiration & liveChatId change
        calls = 0;
        _clock.SetUtcNow(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));

        var gateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingClient = new StubSendClient(async (_, _, _, ct) =>
        {
            calls++;
            if (calls == 1)
            {
                gateStarted.TrySetResult();
                await gate.Task.WaitAsync(ct);
            }

            return new YouTubeInsertMessageResult($"msg-{calls}", _clock.GetUtcNow());
        });

        using var messenger2 = CreateMessenger(blockingClient);
        await messenger2.StartAsync(new Progress<string>(), CancellationToken.None);

        messenger2.Send("blocker", category: ChatSendCategory.CommandReply);
        await gateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        messenger2.Send("ttl-expired", category: ChatSendCategory.CommandReply);
        messenger2.Send("chat-switched", category: ChatSendCategory.ScheduledBroadcast);

        // Advance > 90s and switch liveChatId before releasing blocker
        _clock.Advance(TimeSpan.FromSeconds(95));
        _broadcastSource.CurrentBroadcast.Returns(new YouTubeBroadcastSummary("vid-2", "UCbroadcaster", "Channel", "chat-2", "Live 2", _clock.GetUtcNow()));
        gate.TrySetResult();

        await messenger2.StopAsync(new Progress<string>(), CancellationToken.None);
        Assert.That(calls, Is.EqualTo(1), "Просроченные и относящиеся к старому liveChatId элементы не должны отправляться");
    }

    [Test]
    public async Task Send_Refuses_Response_From_Previous_Live_Chat_Even_Before_Queueing()
    {
        var sends = new List<string>();
        var client = new StubSendClient((chatId, _, _, _) =>
        {
            sends.Add(chatId);
            return Task.FromResult(new YouTubeInsertMessageResult("sent", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(client);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var channel = new ChannelKey(StreamingPlatform.YouTube, "UCbroadcaster");
        var previous = new ChatOrigin(channel, "chat-1");
        _broadcastSource.CurrentBroadcast.Returns(new YouTubeBroadcastSummary("vid-2", channel.ChannelId, "Channel", "chat-2", "Live 2", _clock.GetUtcNow()));

        Assert.That(messenger.Send("old response", origin: previous), Is.EqualTo(ChatEnqueueResult.NotConnected));
        Assert.That(messenger.Send("new response", origin: new(channel, "chat-2")), Is.EqualTo(ChatEnqueueResult.Queued));
        await WaitAndAdvanceUntilAsync(() => sends.Count == 1);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);
        Assert.That(sends, Is.EqualTo(new[] { "chat-2" }));
    }

    [Test]
    public async Task Reply_Prepends_Mention_When_Name_Is_Only_Substring_Of_Word()
    {
        var sentTexts = new List<string>();
        var client = new StubSendClient((_, text, _, _) =>
        {
            sentTexts.Add(text);
            return Task.FromResult(new YouTubeInsertMessageResult($"msg-{sentTexts.Count}", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(client);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        messenger.Reply("msg-in-1", "Al", "Already done");
        await WaitAndAdvanceUntilAsync(() => sentTexts.Count == 1);

        messenger.Reply("msg-in-2", "Al", "Привет, Al!");
        await WaitAndAdvanceUntilAsync(() => sentTexts.Count == 2);

        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(sentTexts[0], Is.EqualTo("@Al Already done"));
            Assert.That(sentTexts[1], Is.EqualTo("Привет, Al!"));
        });
    }

    [Test]
    public async Task Restart_After_StopAsync_And_Item_Exception_Do_Not_Strand_Queue()
    {
        var sentTexts = new List<string>();
        var attempts = 0;
        var client = new StubSendClient((_, text, _, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("unexpected non-API error");
            }

            sentTexts.Add(text);
            return Task.FromResult(new YouTubeInsertMessageResult($"msg-{attempts}", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(client);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(messenger.Send("first", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));
        Assert.That(messenger.Send("second", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));
        await WaitAndAdvanceUntilAsync(() => sentTexts.Count == 1);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);
        Assert.That(messenger.Send("after-restart", category: ChatSendCategory.CommandReply), Is.EqualTo(ChatEnqueueResult.Queued));
        await WaitAndAdvanceUntilAsync(() => sentTexts.Count == 2);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(sentTexts, Is.EqualTo(new[] { "second", "after-restart" }));
    }

    [Test]
    public async Task SendAsAsync_CancelledDuringRateLimitDelay_ReturnsQueueClosed()
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "yt-delay-cancel.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "selected", "Alpha", new YouTubeAccountSettings
        {
            ChannelId = "UC-1",
            ChannelTitle = "Alpha",
            AccessToken = "token-1",
            RefreshToken = "refresh-1",
            StoredScopes = [YouTubeScopes.ForceSsl],
        });

        var sendAttempts = 0;
        var client = new StubSendClient((_, _, _, _) =>
        {
            sendAttempts++;
            return Task.FromResult(new YouTubeInsertMessageResult($"msg-{sendAttempts}", _clock.GetUtcNow()));
        });

        using var messenger = CreateMessenger(client, accounts);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var sender = new ChatSenderAccount("UC-1", PlatformAuthRole.Bot, "selected");
        var first = await messenger.SendAsAsync(sender, "vid-1", "first");
        Assert.That(first.DeliveryStatus, Is.EqualTo(ChatDeliveryStatus.Sent));

        var secondTask = messenger.SendAsAsync(sender, "vid-1", "second");
        await Task.Delay(50);

        messenger.Dispose();

        var second = await secondTask;

        Assert.Multiple(() =>
        {
            Assert.That(sendAttempts, Is.EqualTo(1));
            Assert.That(second.DeliveryStatus, Is.EqualTo(ChatDeliveryStatus.QueueClosed));
            Assert.That(second.SentParts, Is.Zero);
        });
    }

    [Test]
    public async Task AuthHandlers_CheckExpectedAccountId_AndReportUnauthorizedOnMismatch()
    {
        var accounts = new YouTubeAccountsStore(filePath: Path.Combine(_directory, "yt-handler-match.json"));
        accounts.UpsertContext(PlatformAuthRole.Bot, "selected", "Other", new YouTubeAccountSettings
        {
            ChannelId = "UC-OTHER", ChannelTitle = "Other", AccessToken = "bot-token", RefreshToken = "bot-refresh",
        });
        accounts.UpsertContext(PlatformAuthRole.Broadcaster, "bc-ctx", "Owner", new YouTubeAccountSettings
        {
            ChannelId = "UC-OWNER", ChannelTitle = "Owner", AccessToken = "bc-token", RefreshToken = "bc-refresh",
        });

        var auth = new StubPlatformAuthService(
            (PlatformAuthRole.Bot, "selected", "bot-token"),
            (PlatformAuthRole.Broadcaster, "bc-ctx", "bc-token"));

        using var botHandler = new BotYouTubeAuthHandler(
            auth, Microsoft.Extensions.Logging.Abstractions.NullLogger<BotYouTubeAuthHandler>.Instance, accounts)
        {
            InnerHandler = new OkHttpMessageHandler(),
        };
        using var broadcasterHandler = new BroadcasterYouTubeAuthHandler(
            auth, Microsoft.Extensions.Logging.Abstractions.NullLogger<BroadcasterYouTubeAuthHandler>.Instance, accounts)
        {
            InnerHandler = new OkHttpMessageHandler(),
        };
        using var botClient = new HttpClient(botHandler);
        using var broadcasterClient = new HttpClient(broadcasterHandler);

        YouTubeApiException? mismatch;
        using (PlatformAuthRequestOptions.UseContext("selected", "UC-EXPECTED"))
        {
            mismatch = Assert.ThrowsAsync<YouTubeApiException>(
                async () => await botClient.GetAsync("https://www.googleapis.com/youtube/v3/liveChat/messages"));
        }

        HttpResponseMessage broadcasterResponse;
        using (PlatformAuthRequestOptions.UseContext("bc-ctx", "UC-OWNER"))
        {
            broadcasterResponse = await broadcasterClient.GetAsync("https://www.googleapis.com/youtube/v3/liveChat/messages");
        }

        Assert.Multiple(() =>
        {
            Assert.That(mismatch, Is.Not.Null);
            Assert.That(mismatch!.Kind, Is.EqualTo(YouTubeApiErrorKind.Unauthorized));
            Assert.That(broadcasterResponse.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
        });
    }

    private sealed class StubPlatformAuthService(params (PlatformAuthRole Role, string ContextId, string Token)[] tokens)
        : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<string?> GetAccessTokenAsync(
            PlatformAuthRole role, string? contextId, bool forceRefresh = false, CancellationToken ct = default) =>
            ValueTask.FromResult(tokens.FirstOrDefault(t => t.Role == role && t.ContextId == contextId).Token);

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }

    private sealed class OkHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private async Task WaitAndAdvanceUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "Ожидание завершения отправки с продвижением ManualTimeProvider истекло");
    }

    private YouTubeMessenger CreateMessenger(IYouTubeLiveChatSendClient sendClient, YouTubeAccountsStore? accountsStore = null)
    {
        return new YouTubeMessenger(
            sendClient,
            _broadcastSource,
            _targetProvider,
            _quotaGovernor,
            _sentTracker,
            _responseTracker,
            _clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeMessenger>.Instance,
            accountsStore: accountsStore);
    }

    private sealed class StubSendClient(
        Func<string, string, YouTubeQuotaPriority, CancellationToken, Task<YouTubeInsertMessageResult>> handler)
        : IYouTubeLiveChatSendClient
    {
        public Task<YouTubeInsertMessageResult> InsertMessageAsync(
            string liveChatId,
            string messageText,
            YouTubeQuotaPriority priority,
            CancellationToken cancellationToken)
        {
            return handler(liveChatId, messageText, priority, cancellationToken);
        }
    }
}
