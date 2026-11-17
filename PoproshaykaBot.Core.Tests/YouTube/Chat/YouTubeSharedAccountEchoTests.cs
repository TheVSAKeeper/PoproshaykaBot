using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
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
public class YouTubeSharedAccountEchoTests
{
    private const string BroadcasterId = "UCshared0000000000000001";
    private const string DedicatedBotId = "UCbot0000000000000000002";

    private string _directory = null!;
    private ManualTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;
    private IYouTubeLiveBroadcastClient _broadcastClient = null!;
    private StubAuthService _authService = null!;
    private InMemoryEventBus _eventBus = null!;
    private YouTubeSessionContext _sessionContext = null!;
    private YouTubeStreamMonitor _streamMonitor = null!;
    private YouTubeSentMessageTracker _sentTracker = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-echo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.Zero));
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", Channel = BroadcasterId });

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = BroadcasterId, ChannelTitle = "SharedChannel" },
            new YouTubeAccountSettings { ChannelId = BroadcasterId, ChannelTitle = "SharedChannel" });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);

        _broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("vid-1", BroadcasterId, "SharedChannel", "chat-1", "Live", _clock.GetUtcNow())]);

        _authService = new StubAuthService();
        _eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        _sessionContext = new YouTubeSessionContext();
        _sessionContext.Begin(new(BroadcasterId, BroadcasterId, BroadcasterId, "SharedChannel"));

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        _streamMonitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            new InMemoryYouTubeBroadcastSelectionStore(),
            _authService,
            _settingsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance);
        await _streamMonitor.RefreshLiveSnapshotAsync();

        _sentTracker = new YouTubeSentMessageTracker();
    }

    [TearDown]
    public void TearDown()
    {
        _streamMonitor.Dispose();
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task SharedAccount_Early_Echo_Is_Held_While_Viewer_Passes_Immediately_And_Marks_IsBot_After_Insert_Completes()
    {
        var rawEvents = new ConcurrentQueue<RawChatMessageReceived>();
        var viewerSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var echoSeen = new TaskCompletionSource<RawChatMessageReceived>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var rawSub = _eventBus.Subscribe<RawChatMessageReceived>(e =>
        {
            rawEvents.Enqueue(e);

            if (e.Message.Id == "viewer-1")
            {
                viewerSeen.TrySetResult();
            }

            if (e.Message.Id == "echo-early-1")
            {
                echoSeen.TrySetResult(e);
            }
        });

        var insertStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowInsertFinish = new TaskCompletionSource<YouTubeInsertMessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendClient = new StubSendClient(async (_, _, _, ct) =>
        {
            insertStarted.TrySetResult();
            return await allowInsertFinish.Task.WaitAsync(ct);
        });

        var responseTracker = new CommandResponseTracker(_clock);
        var targetProvider = Substitute.For<ITargetChannelProvider>();
        targetProvider.Current.Returns(new TargetChannelState(BroadcasterId, BroadcasterId, IsDebugSession: false, IsSendingAllowed: true));

        using var messenger = new YouTubeMessenger(
            sendClient,
            _streamMonitor,
            targetProvider,
            _quotaGovernor,
            _sentTracker,
            responseTracker,
            _clock,
            NullLogger<YouTubeMessenger>.Instance);
        await messenger.StartAsync(new Progress<string>(), CancellationToken.None);

        var pollingClient = new StubStreamClient((_, _) =>
        [
            new YouTubeLiveChatBatch(
                [
                    TextItem("echo-early-1", BroadcasterId, "SharedChannel", isOwner: true, _clock.GetUtcNow().AddSeconds(1), "ответ бота"),
                    TextItem("viewer-1", "UCviewer9", "Viewer9", isOwner: false, _clock.GetUtcNow().AddSeconds(1), "привет!"),
                ],
                "tok-1",
                TimeSpan.FromSeconds(5),
                null),
        ]);

        using var ingestion = CreateIngestion(pollingClient);

        var mark = new CommandResponseMark("стрим", CommandResponseTarget.Chat);
        messenger.Send("ответ бота", mark, ChatSendCategory.CommandReply);
        await insertStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await viewerSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(rawEvents.Select(x => x.Message.Id), Is.EqualTo(new[] { "viewer-1" }),
            "Зритель должен пройти без задержки, пока раннее эхо ждёт ответа insert");

        allowInsertFinish.TrySetResult(new("echo-early-1", _clock.GetUtcNow()));
        var publishedEcho = await echoSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await ingestion.StopAsync(new Progress<string>(), CancellationToken.None);
        await messenger.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(publishedEcho.Message.IsBot, Is.True);
            Assert.That(responseTracker.TryConsume(StreamingPlatform.YouTube, "echo-early-1", publishedEcho.ChannelKey, publishedEcho.Message.ChatSessionId), Is.EqualTo(mark));
        });
    }

    [Test]
    public async Task SharedAccount_Manual_Broadcaster_Message_During_InFlight_Or_Uncertain_Error_Remains_IsBot_False()
    {
        var received = new ConcurrentQueue<RawChatMessageReceived>();
        var firstSeen = new TaskCompletionSource<RawChatMessageReceived>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSeen = new TaskCompletionSource<RawChatMessageReceived>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(e =>
        {
            received.Enqueue(e);

            if (e.Message.Id == "manual-during-flight")
            {
                firstSeen.TrySetResult(e);
            }

            if (e.Message.Id == "manual-during-timeout")
            {
                secondSeen.TrySetResult(e);
            }
        });

        var pollingClient = new StubStreamClient((_, pageToken) =>
        {
            var id = pageToken is null ? "manual-during-flight" : "manual-during-timeout";
            return
            [
                new YouTubeLiveChatBatch(
                    [TextItem(id, BroadcasterId, "SharedChannel", isOwner: true, _clock.GetUtcNow().AddSeconds(1), "привет от стримера")],
                    "tok-next",
                    TimeSpan.FromSeconds(5),
                    null),
            ];
        });

        using var ingestion = CreateIngestion(pollingClient);

        // Scenario 1: in-flight fails with uncertain outcome -> candidate released as IsBot = false
        using (var scope = _sentTracker.BeginInFlightSend("chat-1"))
        {
            await ingestion.RunSingleIterationAsync(CancellationToken.None);
            Assert.That(received, Is.Empty);
            scope.CompleteUncertain();
        }

        var released1 = await firstSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(released1.Message.IsBot, Is.False);
            Assert.That(released1.Message.IsBroadcaster, Is.True);
        });

        // Scenario 2: in-flight hangs past MaxHoldDuration (2s) -> candidate released as IsBot = false without freezing chat
        using (var hungScope = _sentTracker.BeginInFlightSend("chat-1"))
        {
            await ingestion.RunSingleIterationAsync(CancellationToken.None);
            for (var i = 0; i < 20 && !secondSeen.Task.IsCompleted; i++)
            {
                _clock.Advance(TimeSpan.FromMilliseconds(500));
                await Task.Delay(10);
            }
            Assert.That(secondSeen.Task.IsCompleted, Is.True);
            var released2 = await secondSeen.Task;
            Assert.That(released2.Message.IsBot, Is.False);
        }

        await ingestion.StopAsync(new Progress<string>(), CancellationToken.None);
    }

    [Test]
    public async Task SharedAccount_Broadcast_Switch_Drops_Held_Candidates_Without_Publishing()
    {
        var received = new ConcurrentQueue<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Enqueue);

        var pollingClient = new StubStreamClient((_, _) =>
        [
            new YouTubeLiveChatBatch(
                [TextItem("stale-candidate", BroadcasterId, "SharedChannel", isOwner: true, _clock.GetUtcNow().AddSeconds(1), "старое сообщение")],
                "tok-1",
                TimeSpan.FromSeconds(5),
                null),
        ]);

        using var ingestion = CreateIngestion(pollingClient);

        using var scope = _sentTracker.BeginInFlightSend("chat-1");
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("vid-2", BroadcasterId, "SharedChannel", "chat-2", "Live 2", _clock.GetUtcNow())]);
        await _streamMonitor.RefreshLiveSnapshotAsync();

        scope.CompleteConfirmed("stale-candidate");
        await ingestion.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(received, Is.Empty);
    }

    [Test]
    public async Task DedicatedBotAccount_Never_Delays_Even_When_Insert_Is_InFlight()
    {
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = DedicatedBotId, ChannelTitle = "YTBot" },
            new YouTubeAccountSettings { ChannelId = BroadcasterId, ChannelTitle = "Broadcaster" });
        _sessionContext.Begin(new(BroadcasterId, BroadcasterId, DedicatedBotId, "YTBot"));

        var received = new ConcurrentQueue<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Enqueue);

        var pollingClient = new StubStreamClient((_, _) =>
        [
            new YouTubeLiveChatBatch(
                [TextItem("msg-dedicated", DedicatedBotId, "YTBot", isOwner: false, _clock.GetUtcNow().AddSeconds(1), "сообщение бота")],
                "tok-1",
                TimeSpan.FromSeconds(5),
                null),
        ]);

        using var ingestion = CreateIngestion(pollingClient);

        using var scope = _sentTracker.BeginInFlightSend("chat-1");
        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received.First().Message.IsBot, Is.True);
        });
    }

    [Test]
    public async Task SharedAccount_EchoHoldBuffer_Overflow_Publishes_Oldest_Immediately()
    {
        var received = new ConcurrentQueue<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Enqueue);

        var batchItems = Enumerable.Range(0, 33)
            .Select(i => TextItem($"hold-{i}", BroadcasterId, "SharedChannel", isOwner: true, _clock.GetUtcNow().AddSeconds(1), $"msg-{i}"))
            .ToArray();

        var pollingClient = new StubStreamClient((_, pageToken) =>
            pageToken is null
                ? [new YouTubeLiveChatBatch(batchItems, "tok-1", TimeSpan.FromSeconds(5), null)]
                : []);

        using var ingestion = CreateIngestion(pollingClient);

        using var scope = _sentTracker.BeginInFlightSend("chat-1");
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(1), "Переполнение 32-слотового буфера должно сразу вытолкнуть самый старый элемент");
            Assert.That(received.Single().Message.Id, Is.EqualTo("hold-0"));
        });

        scope.CompleteRejected();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (received.Count < 33 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await ingestion.StopAsync(new Progress<string>(), CancellationToken.None);

        Assert.That(received, Has.Count.EqualTo(33));
    }

    private YouTubeChatIngestionService CreateIngestion(IYouTubeLiveChatStreamClient streamClient)
    {
        var mapper = new YouTubeChatMessageMapper(_sentTracker);
        var pollingClient = Substitute.For<IYouTubeLiveChatPollingClient>();
        return new YouTubeChatIngestionService(
            streamClient,
            pollingClient,
            mapper,
            _streamMonitor,
            _sessionContext,
            _authService,
            _accountsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeChatIngestionService>.Instance,
            _settingsStore,
            _sentTracker);
    }

    private static YouTubeLiveChatItem TextItem(
        string id,
        string authorChannelId,
        string displayName,
        bool isOwner,
        DateTimeOffset publishedAtUtc,
        string text)
    {
        return new(
            id,
            YouTubeLiveChatItemKind.TextMessage,
            publishedAtUtc,
            text,
            text,
            new(authorChannelId, displayName, isOwner, false, false, false),
            "textMessageEvent");
    }

    private sealed class StubStreamClient(
        Func<string, string?, IEnumerable<YouTubeLiveChatBatch>> handler)
        : IYouTubeLiveChatStreamClient
    {
        public async IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();

            foreach (var batch in handler(liveChatId, pageToken))
            {
                yield return batch;
            }
        }
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

    private sealed class StubAuthService : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>("token-ok");

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => true;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }
}
