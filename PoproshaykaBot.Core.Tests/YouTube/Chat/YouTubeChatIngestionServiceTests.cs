using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Handlers;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Grpc.Core;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeChatIngestionServiceTests
{
    private const string ChannelId = "UC0000000000000000000001";

    private string _directory = null!;
    private TestTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;
    private IYouTubeLiveBroadcastClient _broadcastClient = null!;
    private StubAuthService _authService = null!;
    private InMemoryEventBus _eventBus = null!;
    private YouTubeSessionContext _sessionContext = null!;
    private YouTubeStreamMonitor _streamMonitor = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-chat-ingest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", Channel = ChannelId });

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UCbot0000000000000000001", ChannelTitle = "YTBot" },
            new YouTubeAccountSettings { ChannelId = ChannelId, ChannelTitle = "YTBroadcaster" });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);

        _broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("vid-1", ChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow)]);

        _authService = new StubAuthService();

        _eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        _sessionContext = new YouTubeSessionContext();
        _sessionContext.Begin(new(ChannelId, ChannelId, "UCbot0000000000000000001", "YTBot"));

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
    public async Task Streams_Across_Cursor_Reconnect_And_Switches_To_Rest_On_TransportUnsupported()
    {
        var received = new List<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Add);

        var streamCall = 0;
        var streamTokens = new List<string?>();
        var streamClient = new FakeStreamClient(token =>
        {
            streamCall++;
            streamTokens.Add(token);

            if (streamCall == 1)
            {
                return
                [
                    new YouTubeLiveChatBatch(
                        [
                            TextItem("old-1", _clock.UtcNow.AddSeconds(-5), "old command !ping"),
                            TextItem("live-1", _clock.UtcNow.AddSeconds(1), "first live"),
                        ],
                        "tok-1",
                        TimeSpan.FromSeconds(5),
                        null),
                ];
            }

            throw new YouTubeApiException(YouTubeApiErrorKind.TransportUnsupported, "HTTP/2 blocked");
        });

        string? restTokenReceived = null;
        var pollingClient = new FakePollingClient((_, pageToken) =>
        {
            restTokenReceived = pageToken;
            return Task.FromResult(new YouTubeLiveChatBatch(
                [
                    TextItem("live-1", _clock.UtcNow.AddSeconds(1), "duplicate should drop"),
                    TextItem("live-2", _clock.UtcNow.AddSeconds(2), "from rest fallback"),
                ],
                "tok-2",
                TimeSpan.FromSeconds(5),
                null));
        });

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        var diag = ingestion.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(streamTokens, Is.EqualTo(new string?[] { null, "tok-1" }));
            Assert.That(restTokenReceived, Is.EqualTo("tok-1"));
            Assert.That(received.Select(static r => r.Message.Message), Is.EqualTo(new[] { "first live", "from rest fallback" }));
            Assert.That(diag.Transport, Is.EqualTo(YouTubeChatTransport.RestPolling));
            Assert.That(diag.ReceivedMessages, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task QuotaExceeded_On_StreamList_Does_Not_Fall_Back_To_Rest()
    {
        var streamClient = new FakeStreamClient(_ => throw new YouTubeApiException(YouTubeApiErrorKind.QuotaExceeded, "quotaExceeded", "quotaExceeded"));
        var restCalled = false;
        var pollingClient = new FakePollingClient((_, _) =>
        {
            restCalled = true;
            return Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null));
        });

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        var diag = ingestion.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(restCalled, Is.False);
            Assert.That(diag.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(diag.Issue, Is.EqualTo(YouTubeChatIssue.QuotaExhausted));
            Assert.That(_quotaGovernor.CaptureDiagnostics().ExhaustedByServer, Is.True);
        });
    }

    [Test]
    public async Task InvalidPageToken_Publishes_GapEvent_And_Resets_Cursor_Without_Replaying_Seen_Items()
    {
        var gaps = new List<ChatReadingGapDetected>();
        var received = new List<RawChatMessageReceived>();
        using var gSub = _eventBus.Subscribe<ChatReadingGapDetected>(gaps.Add);
        using var rSub = _eventBus.Subscribe<RawChatMessageReceived>(received.Add);

        var call = 0;
        var streamClient = new FakeStreamClient(_ =>
        {
            call++;

            if (call == 1)
            {
                return
                [
                    new YouTubeLiveChatBatch(
                        [TextItem("live-1", _clock.UtcNow, "before gap")],
                        "tok-1",
                        TimeSpan.FromSeconds(5),
                        null),
                ];
            }

            if (call == 2)
            {
                throw new YouTubeApiException(YouTubeApiErrorKind.InvalidPageToken, "expired", "pageTokenInvalid");
            }

            _clock.UtcNow = _clock.UtcNow.AddSeconds(10);
            return
            [
                new YouTubeLiveChatBatch(
                    [
                        TextItem("live-1", _clock.UtcNow.AddSeconds(-10), "before gap"),
                        TextItem("live-2", _clock.UtcNow, "after gap"),
                    ],
                    "tok-2",
                    TimeSpan.FromSeconds(5),
                    null),
            ];
        });

        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(5);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(ingestion.CaptureDiagnostics().GapCount, Is.EqualTo(1));
            Assert.That(received.Select(static r => r.Message.Message), Is.EqualTo(new[] { "before gap", "after gap" }));
        });
    }

    [Test]
    public async Task RestPollingClient_Parses_Messages_And_Enforces_5s_Floor()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(YouTubeEndpoints.BotApiHttpClient).Returns(_ => new HttpClient(new StaticJsonHandler(
            """
            {
              "nextPageToken": "rest-tok",
              "pollingIntervalMillis": 1200,
              "items": [
                {
                  "id": "yt-rest-1",
                  "snippet": {
                    "type": "textMessageEvent",
                    "publishedAt": "2026-11-15T20:00:05Z",
                    "textMessageDetails": { "messageText": "hello rest" }
                  },
                  "authorDetails": {
                    "channelId": "UCu1",
                    "displayName": "Viewer",
                    "isChatOwner": false,
                    "isChatModerator": true,
                    "isChatSponsor": false
                  }
                }
              ]
            }
            """))
        {
            BaseAddress = YouTubeEndpoints.RestApiBaseUri,
        });

        var client = new YouTubeRestLiveChatPollingClient(factory, _quotaGovernor);
        var batch = await client.ListMessagesAsync("chat-1", null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(batch.NextPageToken, Is.EqualTo("rest-tok"));
            Assert.That(batch.PollingInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(batch.Items, Has.Count.EqualTo(1));
            Assert.That(batch.Items[0].MessageText, Is.EqualTo("hello rest"));
            Assert.That(batch.Items[0].Author.IsChatModerator, Is.True);
            Assert.That(_quotaGovernor.CaptureDiagnostics().BotCommittedUnits, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Pipeline_Integration_Processes_YouTube_Message_And_ReadOnlyMessenger_Returns_ObserverMode()
    {
        var services = new ServiceCollection();
        services
            .AddCoreInfrastructure(new UiLogSink(), disposeSerilog: false)
            .AddStatistics()
            .AddSettingsStores()
            .AddDebugChannel(DebugChannelOverride.None)
            .AddTwitchClients()
            .AddYouTubeClients()
            .AddGameBoxArt(cacheOnly: true)
            .AddUserAvatars(cacheOnly: true)
            .AddChatPipeline()
            .AddStreamMonitoring()
            .AddBroadcasting()
            .AddPolls()
            .AddHttpServer()
            .AddObsIntegration()
            .AddSelfUpdate()
            .AddDiagnostics();

        services.AddSingleton<BotConnectionManager>();
        services.AddSingleton<IBotConnectionController>(sp => sp.GetRequiredService<BotConnectionManager>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        var bus = provider.GetRequiredService<IEventBus>();
        var pipeline = provider.GetRequiredService<ChatPipelineHandler>();
        var statsHandler = provider.GetRequiredService<StatisticsTrackingHandler>();
        var users = provider.GetRequiredService<UserStatisticsRepository>();
        var platforms = provider.GetRequiredService<IChatPlatformRegistry>();

        var ytPlatform = platforms.GetRequired(StreamingPlatform.YouTube);
        var enqueueResult = ytPlatform.Messenger.Send("test reply");

        var msg = new ChatMessage(
            StreamingPlatform.YouTube,
            ChannelId,
            "yt-pipe-1",
            "UCviewer77",
            "Viewer77",
            "Viewer77",
            "!стрим",
            [("subscriber", "1")],
            [],
            false,
            false,
            false,
            true);

        await bus.PublishAsync(new RawChatMessageReceived(msg, DateTimeOffset.UtcNow), CancellationToken.None);

        var stats = users.GetPartition(StreamingPlatform.YouTube).GetById("UCviewer77");

        Assert.Multiple(() =>
        {
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(statsHandler, Is.Not.Null);
            Assert.That(enqueueResult, Is.EqualTo(ChatEnqueueResult.ObserverMode));
            Assert.That(stats, Is.Not.Null);
            Assert.That(stats!.MessageCount, Is.EqualTo(1));
            Assert.That(users.GetPartition(StreamingPlatform.Twitch).GetById("UCviewer77"), Is.Null);
        });
    }

    [Test]
    public async Task ChatIngestionStarted_And_Stopped_Publish_On_Connect_ChatEnd_And_Stop()
    {
        var started = new List<ChatIngestionStarted>();
        var stopped = new List<ChatIngestionStopped>();
        using var s1 = _eventBus.Subscribe<ChatIngestionStarted>(started.Add);
        using var s2 = _eventBus.Subscribe<ChatIngestionStopped>(stopped.Add);

        var streamClient = new FakeStreamClient(_ =>
        [
            new YouTubeLiveChatBatch(
                [TextItem("live-1", _clock.UtcNow.AddSeconds(1), "hi")],
                "tok-1",
                TimeSpan.FromSeconds(5),
                null),
        ]);
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(started, Has.Count.EqualTo(1));
            Assert.That(started[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(stopped, Is.Empty);
            Assert.That(ingestion.IsConnected, Is.False);
        });

        await ingestion.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Has.Count.EqualTo(1));
            Assert.That(stopped[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
        });
    }

    [Test]
    public async Task OfflineAt_On_NonEnded_Batch_Does_Not_Trigger_Broadcast_Refresh_Or_Stop()
    {
        var stopped = new List<ChatIngestionStopped>();
        using var sub = _eventBus.Subscribe<ChatIngestionStopped>(stopped.Add);

        _broadcastClient.ClearReceivedCalls();

        var streamClient = new FakeStreamClient(_ => throw new YouTubeApiException(YouTubeApiErrorKind.TransportUnsupported, "no grpc"));
        var pollingClient = new FakePollingClient((_, _) => Task.FromResult(new YouTubeLiveChatBatch(
            [TextItem("live-1", _clock.UtcNow.AddSeconds(1), "still live")],
            "tok-1",
            TimeSpan.FromSeconds(5),
            _clock.UtcNow.AddHours(1))));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        await _broadcastClient.DidNotReceiveWithAnyArgs().ListActiveBroadcastsAsync(default);
        Assert.That(stopped, Is.Empty);
    }

    [Test]
    public async Task MidBatch_Cancel_And_Monitor_Outage_Preserve_Cursor_Without_Dropping_Or_Replaying()
    {
        var delivered = new List<string>();
        using var cts = new CancellationTokenSource();
        var failSecond = true;

        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(e =>
        {
            if (e.Message.Message == "second" && failSecond)
            {
                failSecond = false;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }

            delivered.Add(e.Message.Message);
        });

        var seenTokens = new List<string?>();
        var streamClient = new FakeStreamClient(pageToken =>
        {
            seenTokens.Add(pageToken);
            return
            [
                new YouTubeLiveChatBatch(
                    [
                        TextItem("m-1", _clock.UtcNow.AddSeconds(1), "first"),
                        TextItem("m-2", _clock.UtcNow.AddSeconds(2), "second"),
                    ],
                    "tok-after",
                    TimeSpan.FromSeconds(5),
                    null),
            ];
        });
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        Assert.ThrowsAsync<OperationCanceledException>(() => ingestion.RunSingleIterationAsync(cts.Token));

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<YouTubeBroadcastSummary>());
        await _streamMonitor.RefreshLiveSnapshotAsync();
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("vid-1", ChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow)]);
        await _streamMonitor.RefreshLiveSnapshotAsync();
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(seenTokens, Is.EqualTo(new string?[] { null, null }));
            Assert.That(delivered, Is.EqualTo(new[] { "first", "second" }));
        });
    }

    [Test]
    public async Task Expired_Bot_Token_Refreshes_And_Allows_Chat_Ingestion()
    {
        _settingsStore.Save(new YouTubeSettings
        {
            Enabled = true,
            ProjectId = "proj",
            ClientId = "yt-client",
            Channel = ChannelId,
        });
        _accountsStore.SaveAll(
            "yt-secret",
            new YouTubeAccountSettings
            {
                ChannelId = "UCbot0000000000000000001",
                ChannelTitle = "YTBot",
                AccessToken = "expired-bot",
                RefreshToken = "bot-refresh",
                AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(-10),
            },
            new YouTubeAccountSettings
            {
                ChannelId = ChannelId,
                ChannelTitle = "YTBroadcaster",
                AccessToken = "broadcaster-ok",
                RefreshToken = "broadcaster-refresh",
                AccessTokenExpiresAt = _clock.UtcNow.AddHours(1),
            });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new StaticJsonHandler(
            """{"access_token":"fresh-bot-token","expires_in":3600,"token_type":"Bearer"}""")));

        var options = new YouTubeOAuthOptions
        {
            AuthorizationEndpoint = "http://auth.local/o/oauth2/v2/auth",
            TokenEndpoint = "http://token.local/token",
            ChannelsEndpoint = "http://api.local/youtube/v3/channels",
        };
        var statusReporter = new YouTubeOAuthStatusReporter(NullLogger<YouTubeOAuthStatusReporter>.Instance);
        var tokenClient = new YouTubeOAuthTokenClient(httpFactory, options, NullLogger<YouTubeOAuthTokenClient>.Instance);
        var accountWriter = new YouTubeOAuthAccountWriter(_accountsStore, _eventBus, statusReporter, _clock, NullLogger<YouTubeOAuthAccountWriter>.Instance);
        using var refresher = new YouTubeOAuthTokenRefresher(
            tokenClient,
            accountWriter,
            statusReporter,
            _accountsStore,
            _settingsStore,
            options,
            _eventBus,
            _clock,
            NullLogger<YouTubeOAuthTokenRefresher>.Instance);

        var received = new List<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Add);

        var streamClient = new FakeStreamClient(_ =>
        [
            new YouTubeLiveChatBatch(
                [TextItem("fresh-1", _clock.UtcNow.AddSeconds(1), "after refresh")],
                "tok-1",
                TimeSpan.FromSeconds(5),
                null),
        ]);
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = new YouTubeChatIngestionService(
            streamClient,
            pollingClient,
            new YouTubeChatMessageMapper(),
            _streamMonitor,
            _sessionContext,
            new RefresherAdapter(refresher, accountWriter),
            _accountsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeChatIngestionService>.Instance);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_accountsStore.Load(PlatformAuthRole.Bot).AccessToken, Is.EqualTo("fresh-bot-token"));
            Assert.That(received.Select(static r => r.Message.Message), Is.EqualTo(new[] { "after refresh" }));
        });
    }

    [Test]
    public async Task LocalQuotaBudgetReached_Does_Not_Mark_Server_Exhausted_And_IsConnected_Follows_State()
    {
        var streamClient = new FakeStreamClient(_ =>
            throw new YouTubeApiException(YouTubeApiErrorKind.LocalQuotaBudgetReached, "budget", "localBudgetReached"));
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        await ingestion.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        await ingestion.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        var diag = ingestion.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(diag.State, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(diag.Issue, Is.EqualTo(YouTubeChatIssue.QuotaExhausted));
            Assert.That(ingestion.IsConnected, Is.False);
            Assert.That(_quotaGovernor.CaptureDiagnostics().ExhaustedByServer, Is.False);
        });
    }

    [Test]
    public void YouTubeChatPlatform_IsEnabled_Is_False_In_DebugSession()
    {
        var streamClient = new FakeStreamClient(_ => []);
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));
        using var ingestion = CreateIngestion(streamClient, pollingClient);

        var targetProvider = Substitute.For<ITargetChannelProvider>();
        targetProvider.Current.Returns(new TargetChannelState(ChannelId, ChannelId, IsDebugSession: true, IsSendingAllowed: false));

        var messenger = Substitute.For<IPlatformMessenger>();
        var platform = new YouTubeChatPlatform(
            _streamMonitor,
            ingestion,
            messenger,
            _settingsStore,
            _accountsStore,
            targetProvider);

        Assert.That(platform.IsEnabled, Is.False);

        targetProvider.Current.Returns(new TargetChannelState(ChannelId, ChannelId, IsDebugSession: false, IsSendingAllowed: true));
        Assert.That(platform.IsEnabled, Is.True);
    }

    [Test]
    public async Task Broadcast_Removal_And_Switch_Cancel_Open_Grpc_Stream_And_Block_Stale_Batches()
    {
        var received = new List<RawChatMessageReceived>();
        var fromBReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new List<ChatIngestionStopped>();
        using var rSub = _eventBus.Subscribe<RawChatMessageReceived>(e =>
        {
            received.Add(e);

            if (e.Message.Message == "from B")
            {
                fromBReceived.TrySetResult();
            }
        });
        using var sSub = _eventBus.Subscribe<ChatIngestionStopped>(e =>
        {
            stopped.Add(e);
            stoppedSignal.TrySetResult();
        });

        var source = new MutableBroadcastSource
        {
            CurrentBroadcast = new("vid-a", ChannelId, "YTBroadcaster", "chat-a", "Broadcast A", _clock.UtcNow),
        };

        var streamA = new ControlledStreamClient();
        var streamB = new ControlledStreamClient();
        var router = new RoutedStreamClient(chatId => chatId == "chat-a" ? streamA : streamB);
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(router, pollingClient, source);

        await ingestion.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        await streamA.FirstBatchYielded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        source.CurrentBroadcast = null;
        await _eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None);
        await streamA.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await streamA.Batches.Writer.WriteAsync(new([], "tok-ignored", TimeSpan.FromSeconds(5), null));
        streamA.Batches.Writer.TryComplete();

        Assert.Multiple(() =>
        {
            Assert.That(streamA.Closed.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(stopped, Has.Count.EqualTo(1));
            Assert.That(ingestion.IsConnected, Is.False);
            Assert.That(received, Is.Empty);
        });

        var broadcastB = new YouTubeBroadcastSummary("vid-b", ChannelId, "YTBroadcaster", "chat-b", "Broadcast B", _clock.UtcNow);
        source.CurrentBroadcast = broadcastB;
        await _eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(broadcastB), CancellationToken.None);

        await streamB.FirstBatchYielded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await streamB.Batches.Writer.WriteAsync(new(
            [TextItem("b-1", _clock.UtcNow.AddSeconds(1), "from B")],
            "tok-b",
            TimeSpan.FromSeconds(5),
            null));

        await fromBReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(received.Select(static r => r.Message.Message), Is.EqualTo(new[] { "from B" }));

        var staleBatchArrivedAfterSwitch = new YouTubeLiveChatBatch(
            [TextItem("stale-from-b", _clock.UtcNow.AddSeconds(2), "stale after remove")],
            "tok-b2",
            TimeSpan.FromSeconds(5),
            null);
        source.CurrentBroadcast = null;
        await _eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(null), CancellationToken.None);
        await streamB.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        streamB.Batches.Writer.TryWrite(staleBatchArrivedAfterSwitch);
        streamB.Batches.Writer.TryComplete();

        await ingestion.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);

        Assert.That(received.Select(static r => r.Message.Message), Is.EqualTo(new[] { "from B" }));
    }

    [Test]
    public async Task Stale_Grpc_And_Rest_Batches_Are_Not_Published_When_Broadcast_Cleared_Before_Processing()
    {
        var received = new List<RawChatMessageReceived>();
        using var sub = _eventBus.Subscribe<RawChatMessageReceived>(received.Add);

        var source = new MutableBroadcastSource
        {
            CurrentBroadcast = new("vid-1", ChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow),
        };

        var grpcCall = 0;
        var streamClient = new FakeStreamClient(_ =>
        {
            grpcCall++;

            if (grpcCall == 1)
            {
                source.CurrentBroadcast = null;
                return
                [
                    new YouTubeLiveChatBatch(
                        [TextItem("stale-grpc", _clock.UtcNow.AddSeconds(1), "from-disabled-chat")],
                        "tok-1",
                        TimeSpan.FromSeconds(5),
                        null),
                ];
            }

            throw new YouTubeApiException(YouTubeApiErrorKind.TransportUnsupported, "no grpc");
        });

        var pollingClient = new FakePollingClient((_, _) =>
        {
            source.CurrentBroadcast = null;
            return Task.FromResult(new YouTubeLiveChatBatch(
                [TextItem("stale-rest", _clock.UtcNow.AddSeconds(2), "stale rest message")],
                "tok-rest",
                TimeSpan.FromSeconds(5),
                null));
        });

        using var ingestion = CreateIngestion(streamClient, pollingClient, source);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        source.CurrentBroadcast = new("vid-1", ChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        source.CurrentBroadcast = new("vid-1", ChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.That(received, Is.Empty);
    }

    [Test]
    public async Task Documented_Grpc_ResourceExhausted_Throttle_Does_Not_Exhaust_Daily_Quota()
    {
        var streamClient = new FakeStreamClient(_ =>
            throw YouTubeErrorClassifier.FromRpcException(
                new RpcException(new Status(StatusCode.ResourceExhausted, "Resource has been exhausted (e.g. check quota)")),
                "StreamList"));
        var pollingClient = new FakePollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null)));

        using var ingestion = CreateIngestion(streamClient, pollingClient);

        var delay = await ingestion.RunSingleIterationAsync(CancellationToken.None);
        var diag = ingestion.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(_quotaGovernor.CaptureDiagnostics().ExhaustedByServer, Is.False);
            Assert.That(diag.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(diag.Issue, Is.EqualTo(YouTubeChatIssue.TransientError));
            Assert.That(diag.Transport, Is.Not.EqualTo(YouTubeChatTransport.RestPolling));
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    private YouTubeChatIngestionService CreateIngestion(
        IYouTubeLiveChatStreamClient streamClient,
        IYouTubeLiveChatPollingClient pollingClient,
        IYouTubeActiveBroadcastSource? broadcastSource = null)
    {
        return new(
            streamClient,
            pollingClient,
            new YouTubeChatMessageMapper(),
            broadcastSource ?? _streamMonitor,
            _sessionContext,
            _authService,
            _accountsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeChatIngestionService>.Instance);
    }

    private static YouTubeLiveChatItem TextItem(string id, DateTimeOffset publishedAt, string text)
    {
        return new(
            id,
            YouTubeLiveChatItemKind.TextMessage,
            publishedAt,
            text,
            text,
            new("UCviewer1", "Viewer1", false, false, false, false),
            "textMessageEvent");
    }

    private sealed class FakeStreamClient(Func<string?, IReadOnlyList<YouTubeLiveChatBatch>> produce)
        : IYouTubeLiveChatStreamClient
    {
        public async IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            var batches = produce(pageToken);

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch;
            }
        }
    }

    private sealed class FakePollingClient(Func<string, string?, Task<YouTubeLiveChatBatch>> handler)
        : IYouTubeLiveChatPollingClient
    {
        public Task<YouTubeLiveChatBatch> ListMessagesAsync(string liveChatId, string? pageToken, CancellationToken cancellationToken)
        {
            return handler(liveChatId, pageToken);
        }
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class RefresherAdapter(
        YouTubeOAuthTokenRefresher refresher,
        YouTubeOAuthAccountWriter accountWriter) : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => refresher.HasUsableAccessToken(role);

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => refresher.GetAccessTokenAsync(role, forceRefresh, ct);

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken)
            => accountWriter.TryClearAccessToken(role, failedAccessToken);

        public void ResetRetryState(PlatformAuthRole role)
            => refresher.ResetRetryState(role);
    }

    private sealed class StubAuthService : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(role == PlatformAuthRole.Bot ? "bot-token" : "broadcaster-token");

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }

    private sealed class MutableBroadcastSource : IYouTubeActiveBroadcastSource
    {
        public YouTubeBroadcastSummary? CurrentBroadcast { get; set; }

        public Task RefreshLiveSnapshotAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RoutedStreamClient(Func<string, IYouTubeLiveChatStreamClient> resolve)
        : IYouTubeLiveChatStreamClient
    {
        public IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken)
        {
            return resolve(liveChatId).StreamBatchesAsync(liveChatId, pageToken, cancellationToken);
        }
    }

    private sealed class ControlledStreamClient : IYouTubeLiveChatStreamClient
    {
        public Channel<YouTubeLiveChatBatch> Batches { get; } = Channel.CreateUnbounded<YouTubeLiveChatBatch>();

        public TaskCompletionSource FirstBatchYielded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                yield return new([], "init-tok", TimeSpan.FromSeconds(5), null);
                FirstBatchYielded.TrySetResult();

                await foreach (var batch in Batches.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return batch;
                }
            }
            finally
            {
                Closed.TrySetResult();
            }
        }
    }
}
