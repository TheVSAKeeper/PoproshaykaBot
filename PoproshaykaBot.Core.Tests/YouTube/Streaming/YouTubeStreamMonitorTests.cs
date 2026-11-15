using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Streaming;

[TestFixture]
public class YouTubeStreamMonitorTests
{
    private const string ChannelId = "UC0000000000000000000001";

    private string _directory = null!;
    private TestTimeProvider _clock = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _governor = null!;
    private IYouTubeLiveBroadcastClient _broadcastClient = null!;
    private InMemoryYouTubeBroadcastSelectionStore _selectionStore = null!;
    private StubAuthService _authService = null!;
    private InMemoryEventBus _eventBus = null!;
    private YouTubeStreamMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-monitor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", Channel = ChannelId });

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll("secret", new YouTubeAccountSettings(), new YouTubeAccountSettings { ChannelId = ChannelId });

        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _governor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, _clock, NullLogger<YouTubeQuotaGovernor>.Instance);

        _broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        _selectionStore = new InMemoryYouTubeBroadcastSelectionStore();
        _authService = new StubAuthService { Token = "broadcaster-token" };
        _eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        _monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            _authService,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _monitor.Dispose();
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task Disabled_Or_Missing_Auth_Makes_Zero_Api_Calls()
    {
        _settingsStore.Mutate(s => s.Enabled = false);
        await _monitor.RefreshLiveSnapshotAsync();

        var disabledDiag = _monitor.CaptureDiagnostics();

        _settingsStore.Mutate(s => s.Enabled = true);
        _authService.Token = null;
        await _monitor.RefreshLiveSnapshotAsync();

        var unauthDiag = _monitor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(disabledDiag.Enabled, Is.False);
            Assert.That(disabledDiag.StreamStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(unauthDiag.Issue, Is.EqualTo(YouTubeMonitorIssue.MissingBroadcasterAuth));
        });

        await _broadcastClient.DidNotReceiveWithAnyArgs().ListActiveBroadcastsAsync(default);
    }

    [Test]
    public async Task Zero_Matches_Transitions_To_Offline()
    {
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<YouTubeBroadcastSummary>());

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Offline));
            Assert.That(_monitor.CurrentBroadcast, Is.Null);
            Assert.That(_monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.None));
        });
    }

    [Test]
    public async Task Multiple_Active_Broadcasts_Never_Auto_Pick_And_Wait_For_Selection()
    {
        var multipleEvents = new List<YouTubeMultipleBroadcastsDetected>();
        using var sub = _eventBus.Subscribe<YouTubeMultipleBroadcastsDetected>(multipleEvents.Add);

        var candidates = new[]
        {
            BuildBroadcast("vid-1", "chat-1", "Primary"),
            BuildBroadcast("vid-2", "chat-2", "Vertical Shorts"),
        };

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns(candidates);

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(_monitor.CurrentBroadcast, Is.Null);
            Assert.That(_monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.MultipleActiveBroadcasts));
            Assert.That(_monitor.CaptureDiagnostics().ActiveCandidateCount, Is.EqualTo(2));
            Assert.That(multipleEvents, Has.Count.EqualTo(1));
        });

        _selectionStore.SelectBroadcast(ChannelId, "vid-2");
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-2"));
        });
    }

    [Test]
    public async Task Hidden_Viewers_And_Viewer_Samples_And_Stream_End_Publish_Expected_Events()
    {
        var onlineEvents = new List<StreamWentOnline>();
        var offlineEvents = new List<StreamWentOffline>();
        var viewerEvents = new List<ViewerSampleObserved>();
        var broadcastEvents = new List<YouTubeActiveBroadcastChanged>();

        using var s1 = _eventBus.Subscribe<StreamWentOnline>(onlineEvents.Add);
        using var s2 = _eventBus.Subscribe<StreamWentOffline>(offlineEvents.Add);
        using var s3 = _eventBus.Subscribe<ViewerSampleObserved>(viewerEvents.Add);
        using var s4 = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(broadcastEvents.Add);

        var broadcast = BuildBroadcast("vid-1", "chat-1", "Main Broadcast");
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns([broadcast]);

        await _monitor.RefreshLiveSnapshotAsync();

        var afterDiscovery = _monitor.GetSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(afterDiscovery.ViewerSampledAtUtc, Is.Null);
            Assert.That(onlineEvents, Has.Count.EqualTo(1));
            Assert.That(broadcastEvents[^1].ActiveBroadcast?.VideoId, Is.EqualTo("vid-1"));
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "Updated Title", _clock.UtcNow, null, null));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.GetSnapshot().ViewerSampledAtUtc, Is.Null);
            Assert.That(_monitor.CurrentStream?.Title, Is.EqualTo("Updated Title"));
            Assert.That(viewerEvents, Is.Empty);
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "Updated Title", _clock.UtcNow, null, 250));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.GetSnapshot().ViewerSampledAtUtc, Is.EqualTo(_clock.UtcNow));
            Assert.That(_monitor.CurrentStream?.ViewerCount, Is.EqualTo(250));
            Assert.That(viewerEvents, Has.Count.EqualTo(1));
            Assert.That(viewerEvents[0].ViewerCount, Is.EqualTo(250));
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "Updated Title", _clock.UtcNow, _clock.UtcNow, 0));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Offline));
            Assert.That(_monitor.CurrentBroadcast, Is.Null);
            Assert.That(offlineEvents, Has.Count.EqualTo(1));
            Assert.That(broadcastEvents[^1].ActiveBroadcast, Is.Null);
        });
    }

    [Test]
    public async Task Transient_Error_And_QuotaExceeded_Do_Not_Emit_False_StreamWentOffline()
    {
        var offlineEvents = new List<StreamWentOffline>();
        using var sub = _eventBus.Subscribe<StreamWentOffline>(offlineEvents.Add);

        var broadcast = BuildBroadcast("vid-1", "chat-1", "Live");
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns([broadcast]);
        await _monitor.RefreshLiveSnapshotAsync();

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.Transient, "503"));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(_monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.TransientError));
            Assert.That(offlineEvents, Is.Empty);
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.QuotaExceeded, "quotaExceeded", "quotaExceeded"));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(_monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.QuotaExhausted));
            Assert.That(offlineEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Unknown_To_Offline_And_Disable_While_Online_Publish_StreamWentOffline()
    {
        var offlineEvents = new List<StreamWentOffline>();
        using var sub = _eventBus.Subscribe<StreamWentOffline>(offlineEvents.Add);

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<YouTubeBroadcastSummary>());

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.That(offlineEvents, Has.Count.EqualTo(1));
        Assert.That(offlineEvents[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
        Assert.That(offlineEvents[0].Channel, Is.EqualTo(ChannelId));

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-1", "chat-1", "Live")]);

        await _monitor.RefreshLiveSnapshotAsync();
        Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));

        _settingsStore.Mutate(s => s.Enabled = false);
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(_monitor.CurrentBroadcast, Is.Null);
            Assert.That(offlineEvents, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Metadata_Change_While_Online_Publishes_StreamMetadataResolved()
    {
        var metadataEvents = new List<StreamMetadataResolved>();
        using var sub = _eventBus.Subscribe<StreamMetadataResolved>(metadataEvents.Add);

        var broadcast = BuildBroadcast("vid-1", "chat-1", "Initial Title");
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns([broadcast]);
        await _monitor.RefreshLiveSnapshotAsync();

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "Edited Title", _clock.UtcNow, null, 77));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(metadataEvents, Has.Count.EqualTo(1));
            Assert.That(metadataEvents[0].Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(metadataEvents[0].Channel, Is.EqualTo(ChannelId));
            Assert.That(metadataEvents[0].Stream.Title, Is.EqualTo("Edited Title"));
            Assert.That(metadataEvents[0].Stream.ViewerCount, Is.EqualTo(77));
        });
    }

    [Test]
    public async Task LocalQuotaBudgetReached_Does_Not_Mark_Server_Exhausted_And_Recovers_When_Budget_Raised()
    {
        _settingsStore.Mutate(s => s.DailyQuotaBudget = 0);
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.LocalQuotaBudgetReached, "budget", "localBudgetReached"));

        await _monitor.RefreshLiveSnapshotAsync();

        var exhaustedDiag = _monitor.CaptureDiagnostics();

        _settingsStore.Mutate(s => s.DailyQuotaBudget = 10_000);
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-1", "chat-1", "Recovered")]);

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exhaustedDiag.Issue, Is.EqualTo(YouTubeMonitorIssue.QuotaExhausted));
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.False);
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-1"));
        });
    }

    [Test]
    public async Task Expired_Broadcaster_Token_Refreshes_And_Allows_Monitor_To_Run()
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
            new YouTubeAccountSettings(),
            new YouTubeAccountSettings
            {
                ChannelId = ChannelId,
                ChannelTitle = "MainChannel",
                AccessToken = "expired-token",
                RefreshToken = "refresh-token",
                AccessTokenExpiresAt = _clock.UtcNow.AddMinutes(-10),
            });

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new TokenRefreshHandler()));

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

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            new RefresherAdapter(refresher, accountWriter),
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance);

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-1", "chat-1", "Refreshed Live")]);

        await monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(refresher.HasUsableAccessToken(PlatformAuthRole.Broadcaster), Is.True);
            Assert.That(_accountsStore.Load(PlatformAuthRole.Broadcaster).AccessToken, Is.EqualTo("fresh-broadcaster-token"));
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
        });
    }

    private YouTubeBroadcastSummary BuildBroadcast(string videoId, string liveChatId, string title)
    {
        return new(videoId, ChannelId, "MainChannel", liveChatId, title, _clock.UtcNow);
    }

    private sealed class TokenRefreshHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"fresh-broadcaster-token","expires_in":3600,"token_type":"Bearer"}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
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
        public string? Token { get; set; }

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => !string.IsNullOrWhiteSpace(Token);

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult(Token);

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }
}
