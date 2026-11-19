using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
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
    public async Task Explicit_Selection_Change_Switches_Active_Broadcast_And_Clearing_Returns_To_Multiple()
    {
        var broadcastEvents = new List<YouTubeActiveBroadcastChanged>();
        var multipleEvents = new List<YouTubeMultipleBroadcastsDetected>();
        var onlineEvents = new List<StreamWentOnline>();
        var metadataEvents = new List<StreamMetadataResolved>();
        using var bSub = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(broadcastEvents.Add);
        using var mSub = _eventBus.Subscribe<YouTubeMultipleBroadcastsDetected>(multipleEvents.Add);
        using var oSub = _eventBus.Subscribe<StreamWentOnline>(onlineEvents.Add);
        using var mdSub = _eventBus.Subscribe<StreamMetadataResolved>(metadataEvents.Add);

        var candidates = new[]
        {
            BuildBroadcast("vid-a", "chat-a", "Broadcast A"),
            BuildBroadcast("vid-b", "chat-b", "Broadcast B"),
        };

        var discoveryCalls = 0;
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                discoveryCalls++;
                return candidates;
            });

        _selectionStore.SelectBroadcast(ChannelId, "vid-a");
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-a"));
            Assert.That(discoveryCalls, Is.EqualTo(1));
        });

        _selectionStore.SelectBroadcast(ChannelId, "vid-b");
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-b"));
            Assert.That(_monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo("chat-b"));
            Assert.That(_monitor.CurrentStream?.Id, Is.EqualTo("vid-b"));
            Assert.That(discoveryCalls, Is.EqualTo(2));
            Assert.That(broadcastEvents[^1].ActiveBroadcast?.VideoId, Is.EqualTo("vid-b"));
            Assert.That(onlineEvents.Select(static e => e.Stream?.Id), Is.EqualTo(["vid-a", "vid-b"]));
            Assert.That(metadataEvents, Is.Empty);
        });

        _selectionStore.SelectBroadcast(ChannelId, null);
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Unknown));
            Assert.That(_monitor.CurrentBroadcast, Is.Null);
            Assert.That(_monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.MultipleActiveBroadcasts));
            Assert.That(discoveryCalls, Is.EqualTo(3));
            Assert.That(broadcastEvents[^1].ActiveBroadcast, Is.Null);
            Assert.That(multipleEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Pinning_Already_Active_Broadcast_Preserves_Viewer_Sample_And_Does_Not_Publish_Zero_Metadata()
    {
        var metadataEvents = new List<StreamMetadataResolved>();
        var broadcastEvents = new List<YouTubeActiveBroadcastChanged>();
        using var mdSub = _eventBus.Subscribe<StreamMetadataResolved>(metadataEvents.Add);
        using var bSub = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(broadcastEvents.Add);

        var broadcast = BuildBroadcast("vid-a", "chat-a", "Broadcast A");
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns([broadcast]);
        _broadcastClient.GetVideoLiveSampleAsync("vid-a", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-a", "Broadcast A", _clock.UtcNow, null, 250));

        await _monitor.RefreshLiveSnapshotAsync();
        await _monitor.RefreshLiveSnapshotAsync();

        var sampledAtBeforePin = _monitor.GetSnapshot().ViewerSampledAtUtc;
        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStream?.ViewerCount, Is.EqualTo(250));
            Assert.That(sampledAtBeforePin, Is.EqualTo(_clock.UtcNow));
        });

        _selectionStore.SelectBroadcast(ChannelId, "vid-a");
        await _monitor.RefreshLiveSnapshotAsync();

        var afterPin = _monitor.GetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-a"));
            Assert.That(_monitor.CurrentStream?.ViewerCount, Is.EqualTo(250));
            Assert.That(afterPin.ViewerSampledAtUtc, Is.EqualTo(sampledAtBeforePin));
            Assert.That(broadcastEvents, Has.Count.EqualTo(1));
            Assert.That(metadataEvents, Is.Empty);
        });
    }

    [Test]
    public async Task Wake_During_InFlight_Poll_Triggers_Immediate_Repoll_Without_Waiting_Full_Interval()
    {
        var candidates = new[]
        {
            BuildBroadcast("vid-a", "chat-a", "Broadcast A"),
            BuildBroadcast("vid-b", "chat-b", "Broadcast B"),
        };

        var discoveryCalls = 0;
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref discoveryCalls);
                return (IReadOnlyList<YouTubeBroadcastSummary>)candidates;
            });

        _selectionStore.SelectBroadcast(ChannelId, "vid-a");
        await _monitor.RefreshLiveSnapshotAsync();

        var sampleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var switchedToSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _broadcastClient.GetVideoLiveSampleAsync("vid-a", Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                sampleEntered.TrySetResult();
                await releaseSample.Task.WaitAsync(call.Arg<CancellationToken>()).ConfigureAwait(false);
                return (YouTubeVideoLiveSample?)new YouTubeVideoLiveSample("vid-a", "Broadcast A", _clock.UtcNow, null, 100);
            });

        using var bSub = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(e =>
        {
            if (e.ActiveBroadcast?.VideoId == "vid-b")
            {
                switchedToSecond.TrySetResult();
            }
        });

        await _monitor.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);

        try
        {
            await sampleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _selectionStore.SelectBroadcast(ChannelId, "vid-b");
            releaseSample.TrySetResult();

            await switchedToSecond.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(Volatile.Read(ref discoveryCalls), Is.GreaterThanOrEqualTo(2));
                Assert.That(_monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-b"));
                Assert.That(_monitor.CurrentStream?.Id, Is.EqualTo("vid-b"));
            });
        }
        finally
        {
            releaseSample.TrySetResult();
            await _monitor.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);
        }
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

    [Test]
    public async Task Direct_Switch_And_Initial_Online_Publish_StreamWentOnline_Before_Broadcast_Becomes_Readable()
    {
        var trace = new List<string>();
        using var oSub = _eventBus.Subscribe<StreamWentOnline>(e =>
            trace.Add($"ONLINE:{e.Stream?.Id};current={_monitor.CurrentBroadcast?.VideoId ?? "null"}"));
        using var bSub = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(e =>
            trace.Add($"BROADCAST:{e.ActiveBroadcast?.VideoId ?? "null"};current={_monitor.CurrentBroadcast?.VideoId ?? "null"}"));

        var candidates = new[]
        {
            BuildBroadcast("vid-a", "chat-a", "Broadcast A"),
            BuildBroadcast("vid-b", "chat-b", "Broadcast B"),
        };
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns(candidates);

        _selectionStore.SelectBroadcast(ChannelId, "vid-a");
        await _monitor.RefreshLiveSnapshotAsync();

        _selectionStore.SelectBroadcast(ChannelId, "vid-b");
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.That(trace, Is.EqualTo(new[]
        {
            "ONLINE:vid-a;current=null",
            "BROADCAST:vid-a;current=vid-a",
            "BROADCAST:null;current=null",
            "ONLINE:vid-b;current=null",
            "BROADCAST:vid-b;current=vid-b",
        }));
    }

    [Test]
    public async Task Refresh_PopulatesThumbnailAndPublishesMonitoringStatusChanges()
    {
        var statuses = new List<StreamMonitoringStatusChanged>();
        using var sub = _eventBus.Subscribe<StreamMonitoringStatusChanged>(e => statuses.Add(e));

        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-thumb", "chat-1", "Thumbnail Live")]);

        await _monitor.RefreshLiveSnapshotAsync();

        _settingsStore.Save(new YouTubeSettings
        {
            Enabled = false,
            ProjectId = "proj",
            ClientId = "yt-client",
            Channel = ChannelId,
        });
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Select(s => (s.Platform, s.Role, s.Status)), Is.EqualTo(new[]
            {
                (StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Connected),
                (StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Disconnected),
            }));
        });

        _settingsStore.Save(new YouTubeSettings
        {
            Enabled = true,
            ProjectId = "proj",
            ClientId = "yt-client",
            Channel = ChannelId,
        });
        await _monitor.RefreshLiveSnapshotAsync();

        Assert.That(_monitor.CurrentStream?.ThumbnailUrl, Is.EqualTo("https://i.ytimg.com/vi/vid-thumb/hqdefault.jpg"));
    }

    [Test]
    public async Task MultiChannel_BotOnly_Offline_Uses_Zero_DataApi_Calls_And_Publishes_Bot_Status()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-offline.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = foreignChannelId,
                    DisplayName = "Foreign",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "UC_BOT",
                    BotContextId = "yt-bot-1",
                    BotVerifiedSubjectId = "UC_BOT",
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns((YouTubePublicLiveVideo?)null);

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");
        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_BOT" });

        var statuses = new List<StreamMonitoringStatusChanged>();
        using var statusSub = _eventBus.Subscribe<StreamMonitoringStatusChanged>(statuses.Add);

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        var snap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, foreignChannelId));
        Assert.Multiple(() =>
        {
            Assert.That(snap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(snap.Status, Is.EqualTo(StreamStatus.Offline));
            Assert.That(statuses.Any(s => s.Role == PlatformAuthRole.Bot && s.Status == StreamMonitoringStatus.Connected), Is.True);
        });

        await _broadcastClient.DidNotReceiveWithAnyArgs().ListActiveBroadcastsAsync(default, default);
        await _broadcastClient.DidNotReceiveWithAnyArgs().GetVideoLiveSampleAsync(default!, default, default, default);
    }

    [Test]
    public async Task MultiChannel_BotOnly_Online_And_Subsequent_Poll_And_End()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-online.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = foreignChannelId,
                    DisplayName = "Foreign",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "UC_BOT",
                    BotContextId = "yt-bot-1",
                    BotVerifiedSubjectId = "UC_BOT",
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-bot-1", foreignChannelId, "Foreign", "Bot Discovery Live", _clock.UtcNow));

        var sampleCalls = 0;
        _broadcastClient.GetVideoLiveSampleAsync("vid-bot-1", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sampleCalls++;
                return sampleCalls <= 2
                    ? new YouTubeVideoLiveSample("vid-bot-1", "API Live Title", _clock.UtcNow, null, 333, foreignChannelId, "Foreign", "chat-bot-1")
                    : new YouTubeVideoLiveSample("vid-bot-1", "API Live Title", _clock.UtcNow, _clock.UtcNow, 0, foreignChannelId, "Foreign", "chat-bot-1");
            });

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");
        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_BOT" });

        var onlineEvents = new List<StreamWentOnline>();
        var offlineEvents = new List<StreamWentOffline>();
        var viewerEvents = new List<ViewerSampleObserved>();
        var broadcastEvents = new List<YouTubeActiveBroadcastChanged>();
        var chatAvailabilityEvents = new List<PlatformChatAvailabilityChanged>();
        using var s1 = _eventBus.Subscribe<StreamWentOnline>(onlineEvents.Add);
        using var s2 = _eventBus.Subscribe<StreamWentOffline>(offlineEvents.Add);
        using var s3 = _eventBus.Subscribe<ViewerSampleObserved>(viewerEvents.Add);
        using var s4 = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(broadcastEvents.Add);
        using var s5 = _eventBus.Subscribe<PlatformChatAvailabilityChanged>(chatAvailabilityEvents.Add);

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sampleCalls, Is.EqualTo(1));
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-bot-1"));
            Assert.That(monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo("chat-bot-1"));
            Assert.That(onlineEvents, Has.Count.EqualTo(1));
            Assert.That(viewerEvents, Has.Count.EqualTo(1));
            Assert.That(viewerEvents[0].ViewerCount, Is.EqualTo(333));
            Assert.That(broadcastEvents[^1].ActiveBroadcast?.LiveChatId, Is.EqualTo("chat-bot-1"));
            Assert.That(chatAvailabilityEvents.Any(e => e.Platform == StreamingPlatform.YouTube && e.IsAvailable), Is.True);
        });

        await monitor.RefreshLiveSnapshotAsync();
        Assert.That(sampleCalls, Is.EqualTo(2));
        await publicDiscovery.Received(1).FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>());

        await monitor.RefreshLiveSnapshotAsync();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Offline));
            Assert.That(monitor.CurrentBroadcast, Is.Null);
            Assert.That(offlineEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task MultiChannel_Mixed_Uses_Broadcaster_For_Own_And_BotOnly_For_Foreign()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-mixed.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = ChannelId,
                    DisplayName = "Own",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = foreignChannelId,
                    DisplayName = "Foreign",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = ChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-foreign", foreignChannelId, "Foreign", "Foreign Live", _clock.UtcNow));

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-own", "chat-own", "Own Live")]);
        _broadcastClient.GetVideoLiveSampleAsync("vid-own", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-own", "Own Live", _clock.UtcNow, null, 100));
        _broadcastClient.GetVideoLiveSampleAsync("vid-foreign", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-foreign", "Foreign Live", _clock.UtcNow, null, 200, foreignChannelId, "Foreign", "chat-foreign"));

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "broadcaster-token");
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        var ownSnap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, ChannelId));
        var foreignSnap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, foreignChannelId));

        Assert.Multiple(() =>
        {
            Assert.That(ownSnap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(ownSnap.Status, Is.EqualTo(StreamStatus.Online));
            Assert.That(ownSnap.Stream?.Id, Is.EqualTo("vid-own"));
            Assert.That(foreignSnap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(foreignSnap.Status, Is.EqualTo(StreamStatus.Online));
            Assert.That(foreignSnap.Stream?.Id, Is.EqualTo("vid-foreign"));
        });

        await publicDiscovery.DidNotReceive().FindActiveLiveVideoAsync(ChannelId, Arg.Any<CancellationToken>());
        await publicDiscovery.Received(1).FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MultiChannel_BotOnly_NegativeCases_Unauthorized_Quota_Transient_And_AuthWake()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-neg.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = foreignChannelId,
                    DisplayName = "Foreign",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "UC_BOT",
                    BotContextId = "yt-bot-1",
                    BotVerifiedSubjectId = "UC_BOT",
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");
        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_BOT" });

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-neg", foreignChannelId, "Foreign", "Live", _clock.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("vid-neg", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-neg", "Live", _clock.UtcNow, null, 50, foreignChannelId, "Foreign", "chat-neg"));

        await monitor.RefreshLiveSnapshotAsync();
        Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));

        _broadcastClient.GetVideoLiveSampleAsync("vid-neg", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.Transient, "503"));
        await monitor.RefreshLiveSnapshotAsync();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.TransientError));
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-neg", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401"));
        await monitor.RefreshLiveSnapshotAsync();
        var unauthSnap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, foreignChannelId));
        Assert.Multiple(() =>
        {
            Assert.That(unauthSnap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Unavailable));
            Assert.That(unauthSnap.Reason, Is.EqualTo(MonitoringUnavailableReason.MissingBotAuth));
            Assert.That(monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.MissingBotAuth));
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-neg", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.QuotaExceeded, "quotaExceeded", "quotaExceeded"));
        await monitor.RefreshLiveSnapshotAsync();
        Assert.Multiple(() =>
        {
            Assert.That(monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.QuotaExhausted));
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.True);
        });

        var wokenTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedObservedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unauthSub = _eventBus.Subscribe<ChannelMonitoringStateChanged>(e =>
        {
            if (e.Snapshot.Reason == MonitoringUnavailableReason.MissingBotAuth)
            {
                failedObservedTcs.TrySetResult();
            }
        });
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                wokenTcs.TrySetResult();
                return (YouTubePublicLiveVideo?)null;
            });

        roleAuth.ClearTokens();
        await monitor.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        try
        {
            await failedObservedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "fresh-bot-token");
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));
            await wokenTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await monitor.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);
        }
    }

    [Test]
    public async Task SingleChannel_Legacy_BotOnly_Resolves_Handle_And_Monitors_Hybrid()
    {
        _settingsStore.Save(new YouTubeSettings
        {
            Enabled = true,
            ProjectId = "proj",
            Channel = "@foreignstreamer",
        });

        const string foreignChannelId = "UC0000000000000000000002";
        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-legacy-bot", foreignChannelId, "Foreign", "Legacy Bot Live", _clock.UtcNow));

        _broadcastClient.ResolveChannelIdByHandleAsync("@foreignstreamer", PlatformAuthRole.Bot, null, Arg.Any<CancellationToken>())
            .Returns(foreignChannelId);
        _broadcastClient.GetVideoLiveSampleAsync("vid-legacy-bot", PlatformAuthRole.Bot, null, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-legacy-bot", "Legacy Bot Live", _clock.UtcNow, null, 88, foreignChannelId, "Foreign", "chat-legacy"));

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            accountsStore: _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-legacy-bot"));
            Assert.That(monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo("chat-legacy"));
            Assert.That(monitor.CurrentStream?.ViewerCount, Is.EqualTo(88));
        });
    }

    [Test]
    public async Task MultiChannel_Active_Channel_Switch_Mid_Discovery_Does_Not_Overwrite_Active_With_Old_Channel()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-switch-race.json"));
        const string channelA = "UC000000000000000000000A";
        const string channelB = "UC000000000000000000000B";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = channelA, DisplayName = "Channel A", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = channelB, DisplayName = "Channel B", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = channelA }],
        });

        var sampleEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSample = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(channelA, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-a", channelA, "Channel A", "Live A", _clock.UtcNow));
        publicDiscovery.FindActiveLiveVideoAsync(channelB, Arg.Any<CancellationToken>())
            .Returns((YouTubePublicLiveVideo?)null);

        _broadcastClient.GetVideoLiveSampleAsync("vid-a", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                sampleEntered.TrySetResult();
                await releaseSample.Task.WaitAsync(call.Arg<CancellationToken>()).ConfigureAwait(false);
                return (YouTubeVideoLiveSample?)new YouTubeVideoLiveSample("vid-a", "Live A", _clock.UtcNow, null, 120, channelA, "Channel A", "chat-a");
            });

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        var refreshTask = monitor.RefreshLiveSnapshotAsync();
        await sampleEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        catalogStore.Mutate(s => s.Platforms[0].ActiveChannelId = channelB);
        await _eventBus.PublishAsync(new ActiveChannelChanged(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, channelA),
            new(StreamingPlatform.YouTube, channelB),
            "Channel B",
            ChannelSwitchMode.Manual));
        releaseSample.TrySetResult();
        await refreshTask;

        var snapA = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, channelA));
        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentBroadcast, Is.Null);
            Assert.That(snapA.Status, Is.EqualTo(StreamStatus.Online));
            Assert.That(snapA.Stream?.Id, Is.EqualTo("vid-a"));
        });
    }

    [Test]
    public async Task Becoming_Broadcaster_Eligible_Invalidates_Bot_Discovery_And_Runs_ListActiveBroadcasts()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-role-switch.json"));
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = ChannelId, DisplayName = "Main", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = ChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-bot", ChannelId, "Main", "Bot Live", _clock.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("vid-bot", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-bot", "Bot Live", _clock.UtcNow, null, 50, ChannelId, "Main", "chat-bot"));
        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns([BuildBroadcast("vid-bc", "chat-bc", "Broadcaster Live")]);
        _broadcastClient.GetVideoLiveSampleAsync("vid-bc", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-bc", "Broadcaster Live", _clock.UtcNow, null, 80, ChannelId, "Main", "chat-bc"));

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();
        Assert.That(monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-bot"));

        roleAuth.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bc-token");
        await monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-bc"));
            Assert.That(monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo("chat-bc"));
        });
        await _broadcastClient.Received(1).ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unverified_Or_Drifted_Bot_And_Active_Bot_Failure_Report_AccessUnverified_And_Bot_Role()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-role-status.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = ChannelId, DisplayName = "OwnBg", IsConfigured = true, MonitoringEnabled = true },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = foreignChannelId,
                    DisplayName = "ForeignActive",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "UC_OLD_BOT",
                    BotContextId = "yt-bot-1",
                    BotVerifiedSubjectId = "UC_OLD_BOT",
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_NEW_BOT" });

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bc-token");
        roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<YouTubeBroadcastSummary>());

        var statuses = new List<StreamMonitoringStatusChanged>();
        using var statusSub = _eventBus.Subscribe<StreamMonitoringStatusChanged>(statuses.Add);

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        var snap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, foreignChannelId));
        Assert.Multiple(() =>
        {
            Assert.That(snap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Unavailable));
            Assert.That(snap.Reason, Is.EqualTo(MonitoringUnavailableReason.AccessUnverified));
            Assert.That(monitor.CaptureDiagnostics().Issue, Is.EqualTo(YouTubeMonitorIssue.AccessUnverified));
            Assert.That(statuses.Any(s => s.Role == PlatformAuthRole.Bot), Is.True);
        });
    }

    [Test]
    public async Task Recovery_From_Bot_401_Publishes_Active_ChannelMonitoringStateChanged()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-recovery.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = foreignChannelId, DisplayName = "Foreign", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-rec", foreignChannelId, "Foreign", "Live", _clock.UtcNow));

        var call = 0;
        _broadcastClient.GetVideoLiveSampleAsync("vid-rec", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                call++;
                if (call == 1)
                {
                    throw new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401");
                }

                return new YouTubeVideoLiveSample("vid-rec", "Live", _clock.UtcNow, null, 20, foreignChannelId, "Foreign", "chat-rec");
            });

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var stateChanges = new List<ChannelMonitoringStateChanged>();
        using var sub = _eventBus.Subscribe<ChannelMonitoringStateChanged>(stateChanges.Add);

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();
        Assert.That(stateChanges[^1].Snapshot.Availability, Is.EqualTo(ChannelMonitoringAvailability.Unavailable));

        await monitor.RefreshLiveSnapshotAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stateChanges[^1].Snapshot.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(stateChanges[^1].Snapshot.Reason, Is.EqualTo(MonitoringUnavailableReason.None));
        });
    }

    [Test]
    public async Task Broadcaster_Quota_Hit_Still_Runs_Bot_Zero_Quota_Offline_Discovery()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-quota-isolation.json"));
        const string foreignChannelId = "UC0000000000000000000002";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = ChannelId, DisplayName = "Own", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = foreignChannelId, DisplayName = "Foreign", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignChannelId }],
        });

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.QuotaExceeded, "quotaExceeded", "quotaExceeded"));

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>())
            .Returns((YouTubePublicLiveVideo?)null);

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bc-token");
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        var ownSnap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, ChannelId));
        var foreignSnap = monitor.GetSnapshot(new ChannelKey(StreamingPlatform.YouTube, foreignChannelId));

        Assert.Multiple(() =>
        {
            Assert.That(ownSnap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Unavailable));
            Assert.That(ownSnap.Reason, Is.EqualTo(MonitoringUnavailableReason.QuotaExhausted));
            Assert.That(foreignSnap.Availability, Is.EqualTo(ChannelMonitoringAvailability.Active));
            Assert.That(foreignSnap.Status, Is.EqualTo(StreamStatus.Offline));
        });
        await publicDiscovery.Received(1).FindActiveLiveVideoAsync(foreignChannelId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failing_Background_Bot_Slot_Does_Not_Trigger_Early_Foreground_VideosList()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-cadence.json"));
        const string activeChannel = "UC0000000000000000000002";
        const string bgChannel = "UC0000000000000000000003";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = activeChannel, DisplayName = "Active", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = bgChannel, DisplayName = "Bg", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = activeChannel }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(activeChannel, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-act", activeChannel, "Active", "Live", _clock.UtcNow));

        var bgCalls = 0;
        var secondBgAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstLoopParked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publicDiscovery.FindActiveLiveVideoAsync(bgChannel, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                bgCalls++;
                if (bgCalls == 1)
                {
                    firstLoopParked.TrySetResult();
                    throw new YouTubeApiException(YouTubeApiErrorKind.Transient, "503");
                }

                secondBgAttempt.TrySetResult();
                return (YouTubePublicLiveVideo?)null;
            });

        var activeSampleCalls = 0;
        _broadcastClient.GetVideoLiveSampleAsync("vid-act", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                activeSampleCalls++;
                return new YouTubeVideoLiveSample("vid-act", "Live", _clock.UtcNow, null, 100, activeChannel, "Active", "chat-act");
            });

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            catalogStore,
            _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        try
        {
            await firstLoopParked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _clock.UtcNow += TimeSpan.FromSeconds(15);
            await _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));
            await secondBgAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(Volatile.Read(ref bgCalls), Is.GreaterThanOrEqualTo(2));
                Assert.That(Volatile.Read(ref activeSampleCalls), Is.EqualTo(1));
                Assert.That(monitor.CaptureDiagnostics().ConsecutiveFailures, Is.EqualTo(0));
            });
        }
        finally
        {
            await monitor.StopAsync(new Progress<string>(_ => { }), CancellationToken.None);
        }
    }

    [Test]
    public async Task MidStream_Disabled_Chat_Clears_LiveChatId_And_Publishes_ChatAvailability_False()
    {
        var broadcast = BuildBroadcast("vid-1", "chat-1", "Live");
        _broadcastClient.ListActiveBroadcastsAsync(Arg.Any<CancellationToken>()).Returns([broadcast]);
        await _monitor.RefreshLiveSnapshotAsync();

        var chatAvailabilityEvents = new List<PlatformChatAvailabilityChanged>();
        var broadcastEvents = new List<YouTubeActiveBroadcastChanged>();
        using var s1 = _eventBus.Subscribe<PlatformChatAvailabilityChanged>(chatAvailabilityEvents.Add);
        using var s2 = _eventBus.Subscribe<YouTubeActiveBroadcastChanged>(broadcastEvents.Add);

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample(
                "vid-1",
                "Live",
                _clock.UtcNow,
                null,
                50,
                ChannelId,
                "MainChannel",
                ActiveLiveChatId: null,
                HasLiveStreamingDetails: true));

        await _monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo(string.Empty));
            Assert.That(broadcastEvents, Has.Count.EqualTo(1));
            Assert.That(broadcastEvents[0].ActiveBroadcast?.LiveChatId, Is.EqualTo(string.Empty));
            Assert.That(chatAvailabilityEvents, Has.Count.EqualTo(1));
            Assert.That(chatAvailabilityEvents[0].IsAvailable, Is.False);
        });
    }

    [Test]
    public async Task MultiChannel_Broadcaster_401_Falls_Back_To_Bot_Hybrid_In_Same_Poll()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-bc401.json"));
        catalogStore.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = ChannelId,
                    DisplayName = "OwnChannel",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BroadcasterContextId = AuthContextIds.Default,
                    BotContextId = "yt-bot-1",
                },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = ChannelId }],
        });

        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT" },
            new YouTubeAccountSettings { ChannelId = ChannelId, ChannelTitle = "OwnChannel" });
        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_BOT" });

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .ThrowsAsync(new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401"));

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-bc401-fb", ChannelId, "OwnChannel", "Live via Bot", _clock.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("vid-bc401-fb", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-bc401-fb", "Live via Bot", _clock.UtcNow, null, 77, ChannelId, "OwnChannel", "chat-fb", HasLiveStreamingDetails: true));

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bad-bc-token");
        roleAuth.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "good-bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            channelCatalogStore: catalogStore,
            accountsStore: _accountsStore,
            publicDiscovery: publicDiscovery);

        await monitor.RefreshLiveSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.CurrentStatus, Is.EqualTo(StreamStatus.Online));
            Assert.That(monitor.CurrentBroadcast?.VideoId, Is.EqualTo("vid-bc401-fb"));
            Assert.That(monitor.CurrentBroadcast?.LiveChatId, Is.EqualTo("chat-fb"));
        });
    }

    [Test]
    public async Task MultiChannel_BotOnly_MidStream_Chat_Disable_And_ReEnable_Updates_Broadcast()
    {
        var catalogStore = new ChannelCatalogStore(null, Path.Combine(_directory, "channels-chat-toggle.json"));
        const string foreignId = "UC0000000000000000000055";
        catalogStore.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = foreignId, DisplayName = "Foreign", IsConfigured = true, MonitoringEnabled = true },
            ],
            Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = foreignId }],
        });

        var publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        publicDiscovery.FindActiveLiveVideoAsync(foreignId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-chat-toggle", foreignId, "Foreign", "Live", _clock.UtcNow));

        _broadcastClient.GetVideoLiveSampleAsync("vid-chat-toggle", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(
                new YouTubeVideoLiveSample("vid-chat-toggle", "Live", _clock.UtcNow, null, 20, foreignId, "Foreign", "chat-1", HasLiveStreamingDetails: true),
                new YouTubeVideoLiveSample("vid-chat-toggle", "Live", _clock.UtcNow, null, 25, foreignId, "Foreign", null, HasLiveStreamingDetails: true),
                new YouTubeVideoLiveSample("vid-chat-toggle", "Live", _clock.UtcNow, null, 30, foreignId, "Foreign", "chat-2", HasLiveStreamingDetails: true));

        var roleAuth = new RoleAwareStubAuthService();
        roleAuth.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        var resolver = new YouTubeTargetChannelResolver(_broadcastClient, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        using var monitor = new YouTubeStreamMonitor(
            _broadcastClient,
            resolver,
            _selectionStore,
            roleAuth,
            _settingsStore,
            _governor,
            _eventBus,
            _clock,
            NullLogger<YouTubeStreamMonitor>.Instance,
            channelCatalogStore: catalogStore,
            accountsStore: _accountsStore,
            publicDiscovery: publicDiscovery);

        var chatAvailabilities = new List<bool>();
        using var sub = _eventBus.Subscribe<PlatformChatAvailabilityChanged>(e => chatAvailabilities.Add(e.IsAvailable));

        await monitor.RefreshLiveSnapshotAsync();
        await monitor.RefreshLiveSnapshotAsync();
        var afterDisable = monitor.CurrentBroadcast?.LiveChatId;
        await monitor.RefreshLiveSnapshotAsync();
        var afterReEnable = monitor.CurrentBroadcast?.LiveChatId;

        Assert.Multiple(() =>
        {
            Assert.That(afterDisable, Is.EqualTo(string.Empty));
            Assert.That(afterReEnable, Is.EqualTo("chat-2"));
            Assert.That(chatAvailabilities, Is.EqualTo(new[] { true, false, true }));
        });
    }

    [Test]
    public void QuotaGovernor_Late_Exhaustion_On_Old_Project_Or_Day_Does_Not_Exhaust_New_Project_Or_Day()
    {
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj-old", DailyQuotaBudget = 10_000 });
        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out var oldLease), Is.True);

        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj-new", DailyQuotaBudget = 10_000 });
        oldLease.MarkExhaustedFromServer();
        oldLease.Dispose();

        Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.False);

        Assert.That(_governor.TryReserve(PlatformAuthRole.Bot, YouTubeQuotaPriority.CriticalReadAndMonitor, 1, out var yesterdayLease), Is.True);
        _clock.UtcNow = _clock.UtcNow.AddDays(2);
        yesterdayLease.MarkExhaustedFromServer();
        yesterdayLease.Dispose();

        Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.False);
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

    private sealed class RoleAwareStubAuthService : IPlatformAuthService
    {
        private readonly Dictionary<(PlatformAuthRole Role, string ContextId), string> _tokens = new();

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public void SetToken(PlatformAuthRole role, string contextId, string token)
        {
            _tokens[(role, AuthContextIds.Normalize(contextId))] = token;
        }

        public void ClearTokens() => _tokens.Clear();

        public bool HasUsableAccessToken(PlatformAuthRole role) => HasUsableAccessToken(role, AuthContextIds.Default);

        public bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
            => _tokens.ContainsKey((role, AuthContextIds.Normalize(contextId)));

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => GetAccessTokenAsync(role, AuthContextIds.Default, forceRefresh, ct);

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, string? contextId, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult(_tokens.GetValueOrDefault((role, AuthContextIds.Normalize(contextId))));

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
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
