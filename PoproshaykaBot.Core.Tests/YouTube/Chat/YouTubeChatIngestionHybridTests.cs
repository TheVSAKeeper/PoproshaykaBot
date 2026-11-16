using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Di;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
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
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed class YouTubeChatIngestionHybridTests
{
    private const string BroadcasterChannelId = "UC_BROADCASTER_000001";
    private const string SeparateBotChannelId = "UC_SEPARATE_BOT_000001";

    private string _directory = null!;
    private TestTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _quotaGovernor = null!;
    private InMemoryEventBus _eventBus = null!;
    private YouTubeSessionContext _sessionContext = null!;
    private MutableBroadcastSource _broadcastSource = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pb-yt-hybrid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2025, 11, 16, 16, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = SeparateBotChannelId, ChannelTitle = "YTBot" },
            new YouTubeAccountSettings { ChannelId = BroadcasterChannelId, ChannelTitle = "YTBroadcaster" });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _quotaGovernor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);
        _eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        _sessionContext = new YouTubeSessionContext();
        _sessionContext.Begin(new(BroadcasterChannelId, BroadcasterChannelId, SeparateBotChannelId, "YTBot"));

        _broadcastSource = new MutableBroadcastSource
        {
            CurrentBroadcast = new("SYNTHVID001", BroadcasterChannelId, "YTBroadcaster", "chat-1", "Live", _clock.UtcNow),
        };
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
    public async Task InnerTube_Reads_Without_Bot_OAuth_And_Never_Touches_Quota_Even_On_429()
    {
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        var topSwitchJson = InnerTubeFixtureLoader.ReadText("initial_mixed_with_ticker.json");
        using var comparisonDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("top_vs_live_window_comparison.json"));
        var liveChatJson = comparisonDoc.RootElement.GetProperty("liveChatResponse").GetRawText();
        using var errorsDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("errors_429_403_consent.json"));
        var body429 = errorsDoc.RootElement.GetProperty("rateLimited429Body").GetString()!;

        var httpCall = 0;
        using var handler = new QueueHttpMessageHandler((_, _) =>
        {
            httpCall++;
            return httpCall switch
            {
                1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, topSwitchJson, "application/json"),
                3 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, liveChatJson, "application/json"),
                _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.TooManyRequests, body429, "application/json"),
            };
        });

        var received = new List<RawChatMessageReceived>();
        var gaps = new List<ChatReadingGapDetected>();
        using var rSub = _eventBus.Subscribe<RawChatMessageReceived>(received.Add);
        using var gSub = _eventBus.Subscribe<ChatReadingGapDetected>(gaps.Add);

        var authWithoutBotToken = new MutableAuthService(botToken: null);
        var streamCalls = 0;
        var streamClient = new StubStreamClient((_, _) =>
        {
            streamCalls++;
            return SingleBatch(new(
                [new("LCC.fallback-1", YouTubeLiveChatItemKind.TextMessage, _clock.UtcNow.AddSeconds(1), "Viewer", "fallback ok", new("UC_V", "Viewer", false, false, false, false), "textMessageEvent")],
                "grpc-tok-1",
                TimeSpan.FromSeconds(5),
                null));
        });

        using var ingestion = CreateHybridService(handler, authWithoutBotToken, streamClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(ingestion.CaptureDiagnostics().Transport, Is.EqualTo(YouTubeChatTransport.InnerTube));
            Assert.That(ingestion.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeChatIssue.None));
            Assert.That(streamCalls, Is.EqualTo(0));
            Assert.That(_quotaGovernor.CaptureDiagnostics().CommittedUnits, Is.EqualTo(0));
            Assert.That(_quotaGovernor.CaptureDiagnostics().ReservedUnits, Is.EqualTo(0));
        });

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        var diagAfterFallbackWithoutAuth = ingestion.CaptureDiagnostics();
        Assert.Multiple(() =>
        {
            Assert.That(_quotaGovernor.CaptureDiagnostics().ExhaustedByServer, Is.False);
            Assert.That(_quotaGovernor.CaptureDiagnostics().CommittedUnits, Is.EqualTo(0));
            Assert.That(_quotaGovernor.CaptureDiagnostics().ReservedUnits, Is.EqualTo(0));
            Assert.That(diagAfterFallbackWithoutAuth.Issue, Is.EqualTo(YouTubeChatIssue.MissingBotAuth));
            Assert.That(gaps, Is.Empty);
        });

        authWithoutBotToken.BotToken = "bot-token";
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ingestion.CaptureDiagnostics().Transport, Is.EqualTo(YouTubeChatTransport.GrpcStream));
            Assert.That(streamCalls, Is.EqualTo(1));
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(received, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task SharedAccount_BotEqualsBroadcaster_Skips_InnerTube_And_Uses_OfficialGrpc()
    {
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = BroadcasterChannelId, ChannelTitle = "YTBroadcaster" },
            new YouTubeAccountSettings { ChannelId = BroadcasterChannelId, ChannelTitle = "YTBroadcaster" });
        _sessionContext.Begin(new(BroadcasterChannelId, BroadcasterChannelId, BroadcasterChannelId, "YTBroadcaster"));

        var innerTubeCalled = false;
        using var handler = new QueueHttpMessageHandler((_, _) =>
        {
            innerTubeCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var grpcCalled = false;
        var streamClient = new StubStreamClient((_, _) =>
        {
            grpcCalled = true;
            return SingleBatch(new(
                [new("LCC.official-1", YouTubeLiveChatItemKind.TextMessage, _clock.UtcNow.AddSeconds(1), "Viewer", "hi", new("UC_V", "Viewer", false, false, false, false), "textMessageEvent")],
                "grpc-tok-1",
                TimeSpan.FromSeconds(5),
                null));
        });

        using var ingestion = CreateHybridService(handler, new MutableAuthService("bot-token"), streamClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(innerTubeCalled, Is.False);
            Assert.That(grpcCalled, Is.True);
            Assert.That(ingestion.CaptureDiagnostics().Transport, Is.EqualTo(YouTubeChatTransport.GrpcStream));
            Assert.That(ingestion.CaptureDiagnostics().ReceivedMessages, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InnerTube_ReloadContinuation_Resets_Cursor_And_Publishes_Gap_Without_Cooldown()
    {
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        using var comparisonDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("top_vs_live_window_comparison.json"));
        var liveChatJson = comparisonDoc.RootElement.GetProperty("liveChatResponse").GetRawText();
        using var reloadDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("reload_and_ended.json"));
        var reloadJson = reloadDoc.RootElement.GetProperty("reloadResponse").GetRawText();

        var call = 0;
        using var handler = new QueueHttpMessageHandler((_, _) =>
        {
            call++;
            return call switch
            {
                1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, liveChatJson, "application/json"),
                3 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, reloadJson, "application/json"),
                4 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, liveChatJson, "application/json"),
            };
        });

        var gaps = new List<ChatReadingGapDetected>();
        using var gSub = _eventBus.Subscribe<ChatReadingGapDetected>(gaps.Add);
        var grpcCalled = false;
        var streamClient = new StubStreamClient((_, _) =>
        {
            grpcCalled = true;
            return EmptyStreamAsync();
        });

        using var ingestion = CreateHybridService(handler, new MutableAuthService("bot-token"), streamClient);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(2);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(grpcCalled, Is.False);
            Assert.That(gaps, Has.Count.EqualTo(1));
            Assert.That(ingestion.CaptureDiagnostics().Transport, Is.EqualTo(YouTubeChatTransport.InnerTube));
            Assert.That(call, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Broadcast_Switch_Cancels_Active_InnerTube_Request_Immediately()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handler = new BlockingCancelHandler(requestStarted, requestCancelled);
        var streamClient = new StubStreamClient((_, _) => EmptyStreamAsync());

        using var ingestion = CreateHybridService(handler, new MutableAuthService(botToken: null), streamClient);

        await ingestion.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var switched = new YouTubeBroadcastSummary("SYNTHVID002", BroadcasterChannelId, "YTBroadcaster", "chat-2", "Live 2", _clock.UtcNow);
        _broadcastSource.CurrentBroadcast = switched;
        await _eventBus.PublishAsync(new YouTubeActiveBroadcastChanged(switched), CancellationToken.None);

        await requestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ingestion.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);

        Assert.That(requestCancelled.Task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Repeated_InnerTube_Ended_Still_Signals_Stopped_And_Failed_Probe_Preserves_Official_Cursor()
    {
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        using var endedDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("reload_and_ended.json"));
        var endedJson = endedDoc.RootElement.GetProperty("endedResponse").GetRawText();
        using var errorsDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("errors_429_403_consent.json"));
        var body429 = errorsDoc.RootElement.GetProperty("rateLimited429Body").GetString()!;

        var httpCall = 0;
        using var handler = new QueueHttpMessageHandler((_, _) =>
        {
            httpCall++;
            return httpCall switch
            {
                1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, endedJson, "application/json"),
                3 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                4 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, endedJson, "application/json"),
                _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.TooManyRequests, body429, "application/json"),
            };
        });

        var gaps = new List<ChatReadingGapDetected>();
        using var gSub = _eventBus.Subscribe<ChatReadingGapDetected>(gaps.Add);
        var streamTokens = new List<string?>();
        var streamClient = new StubStreamClient((tok, _) =>
        {
            streamTokens.Add(tok);
            return SingleBatch(new(
                [new("LCC.msg-keep", YouTubeLiveChatItemKind.TextMessage, _clock.UtcNow.AddSeconds(1), "Viewer", "kept", new("UC_V", "Viewer", false, false, false, false), "textMessageEvent")],
                "off-keep-tok",
                TimeSpan.FromSeconds(5),
                null));
        });

        using var ingestion = CreateHybridService(handler, new MutableAuthService("bot-token"), streamClient);

        var delay1 = await ingestion.RunSingleIterationAsync(CancellationToken.None);
        var delay2 = await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(delay1, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(delay2, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(gaps, Is.Empty);
        });

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);

        await ingestion.RunSingleIterationAsync(CancellationToken.None);
        await ingestion.RunSingleIterationAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(streamTokens, Is.EqualTo(new string?[] { null, "off-keep-tok" }));
            Assert.That(gaps, Is.Empty);
            Assert.That(ingestion.CaptureDiagnostics().GapCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Di_Container_Resolves_Stateless_InnerTube_And_IngestionService()
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
        services.AddSingleton<IBotConnectionController>(static sp => sp.GetRequiredService<BotConnectionManager>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        var reader = provider.GetRequiredService<IYouTubeLiveChatReader>();
        var ingestion = provider.GetRequiredService<YouTubeChatIngestionService>();

        Assert.Multiple(() =>
        {
            Assert.That(reader, Is.TypeOf<CompositeYouTubeLiveChatReader>());
            Assert.That(ingestion, Is.Not.Null);
        });
    }

    private YouTubeChatIngestionService CreateHybridService(
        HttpMessageHandler innerTubeHandler,
        IPlatformAuthService authService,
        IYouTubeLiveChatStreamClient streamClient)
    {
        var innerTube = new InnerTubeYouTubeLiveChatReader(
            new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(innerTubeHandler)),
            timeProvider: _clock);
        var grpc = new OfficialGrpcYouTubeLiveChatReader(streamClient);
        var rest = new OfficialRestYouTubeLiveChatReader(new StubPollingClient(), _clock);
        var composite = new CompositeYouTubeLiveChatReader(innerTube, grpc, rest, _clock);

        return new YouTubeChatIngestionService(
            composite,
            new YouTubeChatMessageMapper(),
            _broadcastSource,
            _sessionContext,
            authService,
            _accountsStore,
            _quotaGovernor,
            _eventBus,
            _clock,
            NullLogger<YouTubeChatIngestionService>.Instance,
            _settingsStore);
    }

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> EmptyStreamAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> SingleBatch(YouTubeLiveChatBatch batch)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return batch;
    }

    private sealed class BlockingCancelHandler(
        TaskCompletionSource started,
        TaskCompletionSource cancelled) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "text/html"),
            };
        }
    }

    private sealed class MutableBroadcastSource : IYouTubeActiveBroadcastSource
    {
        public YouTubeBroadcastSummary? CurrentBroadcast { get; set; }

        public Task RefreshLiveSnapshotAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableAuthService(string? botToken) : IPlatformAuthService
    {
        public string? BotToken { get; set; } = botToken;

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) =>
            role != PlatformAuthRole.Bot || !string.IsNullOrWhiteSpace(BotToken);

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default) =>
            ValueTask.FromResult(role == PlatformAuthRole.Bot ? BotToken : "broadcaster-token");

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }

    private sealed class StubStreamClient(
        Func<string?, CancellationToken, IAsyncEnumerable<YouTubeLiveChatBatch>> handler) : IYouTubeLiveChatStreamClient
    {
        public IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken) => handler(pageToken, cancellationToken);
    }

    private sealed class StubPollingClient : IYouTubeLiveChatPollingClient
    {
        public Task<YouTubeLiveChatBatch> ListMessagesAsync(
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null));
    }
}
