using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube;

[TestFixture]
public class YouTubeChannelAdapterTests
{
    private const string ForeignChannelId = "UC0000000000000000000001";
    private const string OwnChannelId = "UC0000000000000000000099";
    private const string BotChannelId = "UC_BOT";

    private string _directory = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private IYouTubeLiveBroadcastClient _broadcastClient = null!;
    private IYouTubeChannelIdentityClient _identityClient = null!;
    private IYouTubeBroadcastSelectionStore _selectionStore = null!;
    private IYouTubePublicLiveDiscoveryClient _publicDiscovery = null!;
    private StubYouTubeAuthService _authService = null!;
    private YouTubeChannelAdapter _adapter = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll("secret", new YouTubeAccountSettings { ChannelId = BotChannelId }, new YouTubeAccountSettings());
        _broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        _identityClient = Substitute.For<IYouTubeChannelIdentityClient>();
        _selectionStore = new InMemoryYouTubeBroadcastSelectionStore();
        _publicDiscovery = Substitute.For<IYouTubePublicLiveDiscoveryClient>();
        _authService = new StubYouTubeAuthService();

        _adapter = new YouTubeChannelAdapter(
            _broadcastClient,
            _identityClient,
            _selectionStore,
            _accountsStore,
            _authService,
            NullLogger<YouTubeChannelAdapter>.Instance,
            _publicDiscovery);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task ResolveChannelIdentity_Uses_Bot_When_Broadcaster_Absent_Falls_Back_To_Broadcaster_And_Returns_Null_Without_Tokens()
    {
        _authService.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");
        _broadcastClient.ResolveChannelIdByHandleAsync("@streamer", PlatformAuthRole.Bot, "yt-bot-1", Arg.Any<CancellationToken>())
            .Returns(ForeignChannelId);

        var botResolved = await _adapter.ResolveChannelIdentityAsync("@streamer", "yt-bot-1", CancellationToken.None);

        Assert.That(botResolved, Is.Not.Null);
        Assert.That(botResolved!.Channel.ChannelId, Is.EqualTo(ForeignChannelId));

        _authService.ClearTokens();
        _authService.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "broadcaster-token");
        _broadcastClient.ResolveChannelIdByHandleAsync("@other", Arg.Any<CancellationToken>())
            .Returns(OwnChannelId);

        var broadcasterResolved = await _adapter.ResolveChannelIdentityAsync("@other", null, CancellationToken.None);

        Assert.That(broadcasterResolved, Is.Not.Null);
        Assert.That(broadcasterResolved!.Channel.ChannelId, Is.EqualTo(OwnChannelId));

        _authService.ClearTokens();
        var unresolved = await _adapter.ResolveChannelIdentityAsync("@nobody", null, CancellationToken.None);

        Assert.That(unresolved, Is.Null);
    }

    [Test]
    public async Task ProbeChannelLive_BotOnly_Online_With_And_Without_Chat()
    {
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");
        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-1", ForeignChannelId, "Creator", "Stream Title", DateTimeOffset.UtcNow));

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "API Title", DateTimeOffset.UtcNow, null, 120, ForeignChannelId, "Creator", "chat-1"));

        var channel = CreateChannelEntry(ForeignChannelId);
        var withChat = await _adapter.ProbeChannelLiveAsync(channel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(withChat.IsLive, Is.True);
            Assert.That(withChat.IsChatReachable, Is.True);
            Assert.That(withChat.ChatSessionToken, Is.EqualTo("chat-1"));
            Assert.That(withChat.Stream?.Title, Is.EqualTo("API Title"));
            Assert.That(withChat.Stream?.ViewerCount, Is.EqualTo(120));
        });

        _broadcastClient.GetVideoLiveSampleAsync("vid-1", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-1", "API Title", DateTimeOffset.UtcNow, null, 120, ForeignChannelId, "Creator", null));

        var withoutChat = await _adapter.ProbeChannelLiveAsync(channel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(withoutChat.IsLive, Is.True);
            Assert.That(withoutChat.IsChatReachable, Is.False);
            Assert.That(withoutChat.ChatSessionToken, Is.Null);
        });
    }

    [Test]
    public async Task ProbeChannelLive_BotOnly_Offline_Uses_Zero_Data_Api_Quota()
    {
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");
        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns((YouTubePublicLiveVideo?)null);

        var result = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(ForeignChannelId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsLive, Is.False);
            Assert.That(result.IsChatReachable, Is.True);
        });

        await _broadcastClient.DidNotReceiveWithAnyArgs().GetVideoLiveSampleAsync(default!, default, default, default);
        await _broadcastClient.DidNotReceiveWithAnyArgs().ListActiveBroadcastsAsync(default, default);
    }

    [Test]
    public async Task ProbeChannelLive_Prioritizes_Broadcaster_For_Own_Channel_And_Falls_Back_To_Bot_For_Foreign()
    {
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT" },
            new YouTubeAccountSettings { ChannelId = OwnChannelId, ChannelTitle = "Owner" });

        _authService.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "broadcaster-token");
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns([new YouTubeBroadcastSummary("own-vid", OwnChannelId, "Owner", "own-chat", "Own Stream", DateTimeOffset.UtcNow)]);

        var ownResult = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(OwnChannelId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ownResult.IsLive, Is.True);
            Assert.That(ownResult.IsChatReachable, Is.True);
            Assert.That(ownResult.Stream?.Id, Is.EqualTo("own-vid"));
        });

        await _publicDiscovery.DidNotReceive().FindActiveLiveVideoAsync(OwnChannelId, Arg.Any<CancellationToken>());

        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("foreign-vid", ForeignChannelId, "Foreign", "Foreign Live", DateTimeOffset.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("foreign-vid", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("foreign-vid", "Foreign Live", DateTimeOffset.UtcNow, null, 50, ForeignChannelId, "Foreign", "foreign-chat"));

        var foreignResult = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(ForeignChannelId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(foreignResult.IsLive, Is.True);
            Assert.That(foreignResult.IsChatReachable, Is.True);
            Assert.That(foreignResult.Stream?.Id, Is.EqualTo("foreign-vid"));
            Assert.That(foreignResult.ChatSessionToken, Is.EqualTo("foreign-chat"));
        });
    }

    [Test]
    public async Task ProbeChannelLive_Without_Any_Auth_Returns_Clear_Failure_Reason()
    {
        var result = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(ForeignChannelId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsLive, Is.False);
            Assert.That(result.IsChatReachable, Is.False);
            Assert.That(result.FailureReason, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task ResolveChannelIdentity_Preserves_Named_Broadcaster_Context_And_Falls_Back_Past_401()
    {
        _authService.SetToken(PlatformAuthRole.Broadcaster, "yt-bc-2", "bc-token");
        _broadcastClient.ResolveChannelIdByHandleAsync("@named", PlatformAuthRole.Broadcaster, "yt-bc-2", Arg.Any<CancellationToken>())
            .Returns(ForeignChannelId);

        var namedResult = await _adapter.ResolveChannelIdentityAsync("@named", "yt-bc-2", CancellationToken.None);

        Assert.That(namedResult?.Channel.ChannelId, Is.EqualTo(ForeignChannelId));

        _authService.ClearTokens();
        _authService.SetToken(PlatformAuthRole.Bot, "yt-bot-bad", "bad-bot");
        _authService.SetToken(PlatformAuthRole.Broadcaster, "yt-bc-good", "good-bc");
        _accountsStore.UpsertContext(PlatformAuthRole.Broadcaster, "yt-bc-good", "Good BC", new YouTubeAccountSettings());
        _broadcastClient.ResolveChannelIdByHandleAsync("@fallback", PlatformAuthRole.Bot, "yt-bot-bad", Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401"));
        _broadcastClient.ResolveChannelIdByHandleAsync("@fallback", PlatformAuthRole.Broadcaster, "yt-bc-good", Arg.Any<CancellationToken>())
            .Returns(OwnChannelId);

        var fallbackResult = await _adapter.ResolveChannelIdentityAsync("@fallback", "yt-bot-bad", CancellationToken.None);

        Assert.That(fallbackResult?.Channel.ChannelId, Is.EqualTo(OwnChannelId));
    }

    [Test]
    public async Task ProbeChannelLive_Broadcaster_401_Falls_Back_To_Bot_Hybrid_And_Blank_Broadcaster_Does_Not_Suppress_Bot()
    {
        _authService.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bc-token");
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");

        _broadcastClient.ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<YouTubeBroadcastSummary>>(_ => throw new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "401"));
        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-bot-fb", ForeignChannelId, "Foreign", "Bot Hybrid", DateTimeOffset.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("vid-bot-fb", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-bot-fb", "Bot Hybrid", DateTimeOffset.UtcNow, null, 75, ForeignChannelId, "Foreign", "chat-fb"));

        var channel = CreateChannelEntry(ForeignChannelId);
        var blankBroadcasterResult = await _adapter.ProbeChannelLiveAsync(channel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(blankBroadcasterResult.IsLive, Is.True);
            Assert.That(blankBroadcasterResult.IsChatReachable, Is.True);
            Assert.That(blankBroadcasterResult.Stream?.Id, Is.EqualTo("vid-bot-fb"));
        });
        await _broadcastClient.DidNotReceive().ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>());

        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT" },
            new YouTubeAccountSettings { ChannelId = ForeignChannelId });

        var fortyOneFallbackResult = await _adapter.ProbeChannelLiveAsync(channel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fortyOneFallbackResult.IsLive, Is.True);
            Assert.That(fortyOneFallbackResult.IsChatReachable, Is.True);
            Assert.That(fortyOneFallbackResult.Stream?.Id, Is.EqualTo("vid-bot-fb"));
        });
        await _broadcastClient.Received(1).ListActiveBroadcastsAsync(AuthContextIds.Default, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProbeChannelLive_Unverified_Or_Drifted_Bot_Assignment_Returns_Not_Chat_Reachable()
    {
        _authService.SetToken(PlatformAuthRole.Bot, "yt-bot-1", "bot-token");
        _accountsStore.UpsertContext(PlatformAuthRole.Bot, "yt-bot-1", "Bot", new YouTubeAccountSettings { ChannelId = "UC_ACTUAL_BOT" });

        var unverifiedChannel = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.YouTube,
            ChannelId = ForeignChannelId,
            DisplayName = "Foreign",
            IsConfigured = true,
            MonitoringEnabled = true,
            BotAccountId = "UC_ACTUAL_BOT",
            BotContextId = "yt-bot-1",
            BotVerifiedSubjectId = null,
        };

        var unverifiedResult = await _adapter.ProbeChannelLiveAsync(unverifiedChannel, CancellationToken.None);

        var driftedChannel = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.YouTube,
            ChannelId = ForeignChannelId,
            DisplayName = "Foreign",
            IsConfigured = true,
            MonitoringEnabled = true,
            BotAccountId = "UC_OLD_BOT",
            BotContextId = "yt-bot-1",
            BotVerifiedSubjectId = "UC_OLD_BOT",
        };

        var driftedResult = await _adapter.ProbeChannelLiveAsync(driftedChannel, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unverifiedResult.IsLive, Is.False);
            Assert.That(unverifiedResult.IsChatReachable, Is.False);
            Assert.That(unverifiedResult.FailureReason, Does.Contain("не подтверждён"));
            Assert.That(driftedResult.IsLive, Is.False);
            Assert.That(driftedResult.IsChatReachable, Is.False);
            Assert.That(driftedResult.FailureReason, Does.Contain("Авторизация бота изменилась"));
        });
    }

    [Test]
    public async Task ProbeChannelLive_Unstarted_Or_Mismatched_Bot_Sample_Returns_Offline()
    {
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");
        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-up", ForeignChannelId, "Creator", "Upcoming", null));

        _broadcastClient.GetVideoLiveSampleAsync("vid-up", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-up", "Upcoming", ActualStartTimeUtc: null, ActualEndTimeUtc: null, ConcurrentViewers: null, ForeignChannelId, "Creator", "chat-1"));

        var unstartedResult = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(ForeignChannelId), CancellationToken.None);

        _broadcastClient.GetVideoLiveSampleAsync("vid-up", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-up", "Wrong", DateTimeOffset.UtcNow, null, 10, ChannelId: OwnChannelId, "Other", "chat-1"));

        var mismatchedResult = await _adapter.ProbeChannelLiveAsync(CreateChannelEntry(ForeignChannelId), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unstartedResult.IsLive, Is.False);
            Assert.That(mismatchedResult.IsLive, Is.False);
        });
    }

    [Test]
    public async Task ProbeChannelLive_Unassigned_Bot_Detects_Live_But_Reports_Chat_Not_Reachable_And_Blank_Verified_Broadcaster_Rejected()
    {
        _authService.SetToken(PlatformAuthRole.Bot, AuthContextIds.Default, "bot-token");
        _publicDiscovery.FindActiveLiveVideoAsync(ForeignChannelId, Arg.Any<CancellationToken>())
            .Returns(new YouTubePublicLiveVideo("vid-unassigned", ForeignChannelId, "Creator", "Live", DateTimeOffset.UtcNow));
        _broadcastClient.GetVideoLiveSampleAsync("vid-unassigned", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns(new YouTubeVideoLiveSample("vid-unassigned", "Live", DateTimeOffset.UtcNow, null, 25, ForeignChannelId, "Creator", "chat-1"));

        var unassignedEntry = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.YouTube,
            ChannelId = ForeignChannelId,
            DisplayName = "Foreign",
            IsConfigured = true,
            MonitoringEnabled = true,
        };

        var unassignedResult = await _adapter.ProbeChannelLiveAsync(unassignedEntry, CancellationToken.None);

        _authService.ClearTokens();
        _authService.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "bc-token");
        var verifiedBlankBroadcaster = new ChannelCatalogEntry
        {
            Platform = StreamingPlatform.YouTube,
            ChannelId = OwnChannelId,
            DisplayName = "Owner",
            IsConfigured = true,
            MonitoringEnabled = true,
            BroadcasterAccountId = OwnChannelId,
            BroadcasterVerifiedSubjectId = OwnChannelId,
        };

        var blankBroadcasterResult = await _adapter.ProbeChannelLiveAsync(verifiedBlankBroadcaster, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unassignedResult.IsLive, Is.True);
            Assert.That(unassignedResult.IsChatReachable, Is.False);
            Assert.That(unassignedResult.FailureReason, Does.Contain("Подтвердите доступ"));
            Assert.That(blankBroadcasterResult.IsLive, Is.False);
            Assert.That(blankBroadcasterResult.FailureReason, Does.Contain("Авторизация ведущего изменилась"));
        });
    }

    [Test]
    public async Task ResolveChannelIdentity_Default_Broadcaster_Overrides_Outer_Ambient_Context()
    {
        _authService.SetToken(PlatformAuthRole.Broadcaster, AuthContextIds.Default, "default-bc");
        string? observedAmbient = null;
        _broadcastClient.ResolveChannelIdByHandleAsync("@ambient", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                observedAmbient = PlatformAuthRequestOptions.CurrentContextId;
                return OwnChannelId;
            });

        using var outer = PlatformAuthRequestOptions.UseContext("yt-other-named");
        var resolved = await _adapter.ResolveChannelIdentityAsync("@ambient", null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(resolved?.Channel.ChannelId, Is.EqualTo(OwnChannelId));
            Assert.That(observedAmbient, Is.EqualTo(AuthContextIds.Default));
        });
    }

    private static ChannelCatalogEntry CreateChannelEntry(string channelId) => new()
    {
        Platform = StreamingPlatform.YouTube,
        ChannelId = channelId,
        DisplayName = channelId,
        IsConfigured = true,
        MonitoringEnabled = true,
        BotAccountId = BotChannelId,
        BotVerifiedSubjectId = BotChannelId,
    };

    private sealed class StubYouTubeAuthService : IPlatformAuthService
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
}
