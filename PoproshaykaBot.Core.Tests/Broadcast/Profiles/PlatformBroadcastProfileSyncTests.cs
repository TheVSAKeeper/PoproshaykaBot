using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.Broadcast.Profiles;

[TestFixture]
public sealed class PlatformBroadcastProfileSyncTests
{
    private string _tempDir = null!;
    private BroadcastProfilesStore _profilesStore = null!;
    private BroadcastProfilePlatformsStore _platformsStore = null!;
    private BroadcastProfileYouTubeStore _youtubeStore = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "platform-profile-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _profilesStore = new(filePath: Path.Combine(_tempDir, "broadcast-profiles.json"));
        _platformsStore = new(filePath: Path.Combine(_tempDir, "broadcast-profile-platforms.json"));
        _youtubeStore = new(filePath: Path.Combine(_tempDir, "broadcast-profile-youtube.json"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void TwitchApplier_EvaluateSync_Covers_Synced_Template_TitleDrift_GameDrift_And_Pending()
    {
        var channelApplier = new ChannelInformationApplier(
            Substitute.For<ITwitchChannelsApi>(),
            Substitute.For<IBroadcasterIdProvider>(),
            Substitute.For<ITargetChannelProvider>(),
            Substitute.For<IChannelUpdateConfirmation>(),
            Substitute.For<IEventBus>(),
            NullLogger<ChannelInformationApplier>.Instance);
        var applier = new TwitchBroadcastProfileApplier(channelApplier);

        var profile = new BroadcastProfile
        {
            Name = "Сюжет",
            Title = "Прохождение #{n}",
            GameId = "509658",
            CurrentNumber = 4,
        };

        var synced = applier.EvaluateSync(profile, new StreamInfo
        {
            Title = "Прохождение #4",
            GameId = "509658",
            GameName = "Just Chatting",
            ViewerCount = 10,
        });

        var titleDrift = applier.EvaluateSync(profile, new StreamInfo
        {
            Title = "Другой эфир",
            GameId = "509658",
            GameName = "Just Chatting",
            ViewerCount = 10,
        });

        var gameDrift = applier.EvaluateSync(profile, new StreamInfo
        {
            Title = "Прохождение #4",
            GameId = "999",
            GameName = "Other Game",
            ViewerCount = 10,
        });

        var bare = applier.EvaluateSync(profile, new StreamInfo());
        var offline = applier.EvaluateSync(profile, null);

        Assert.Multiple(() =>
        {
            Assert.That(synced.State, Is.EqualTo(BroadcastProfileSyncState.Synced));
            Assert.That(titleDrift.State, Is.EqualTo(BroadcastProfileSyncState.Drifted));
            Assert.That(gameDrift.State, Is.EqualTo(BroadcastProfileSyncState.Drifted));
            Assert.That(bare.State, Is.EqualTo(BroadcastProfileSyncState.Pending));
            Assert.That(offline.State, Is.EqualTo(BroadcastProfileSyncState.Pending));
        });
    }

    [Test]
    public void YouTubeApplier_EvaluateSync_Uses_TitleOverride_And_ProfileTitle_And_Pending_When_Offline()
    {
        var profile = new BroadcastProfile
        {
            Name = "Мульти",
            Title = "Общее название",
            CurrentNumber = 7,
        };
        _youtubeStore.SetMetadata(profile.Id, new BroadcastProfileYouTubeMetadata
        {
            TitleOverride = "YouTube выпуск #{n}",
        });

        var applier = new YouTubeBroadcastProfileApplier(
            new ChannelCatalogStore(filePath: Path.Combine(_tempDir, "channels.json")),
            new YouTubeAccountsStore(filePath: Path.Combine(_tempDir, "yt-accounts.json")),
            Substitute.For<IYouTubeActiveBroadcastSource>(),
            Substitute.For<IYouTubeLiveBroadcastClient>(),
            NullLogger<YouTubeBroadcastProfileApplier>.Instance,
            _youtubeStore);

        var syncedOverride = applier.EvaluateSync(profile, new StreamInfo
        {
            Title = "YouTube выпуск #7",
            ViewerCount = 5,
        });

        var driftedOverride = applier.EvaluateSync(profile, new StreamInfo
        {
            Title = "Общее название",
            ViewerCount = 5,
        });

        var offline = applier.EvaluateSync(profile, null);

        var profileWithoutOverride = new BroadcastProfile
        {
            Name = "Без переопределения",
            Title = "Единый заголовок",
        };
        var syncedFallback = applier.EvaluateSync(profileWithoutOverride, new StreamInfo
        {
            Title = "Единый заголовок",
            ViewerCount = 1,
        });

        Assert.Multiple(() =>
        {
            Assert.That(syncedOverride.State, Is.EqualTo(BroadcastProfileSyncState.Synced));
            Assert.That(driftedOverride.State, Is.EqualTo(BroadcastProfileSyncState.Drifted));
            Assert.That(offline.State, Is.EqualTo(BroadcastProfileSyncState.Pending));
            Assert.That(syncedFallback.State, Is.EqualTo(BroadcastProfileSyncState.Synced));
        });
    }

    [Test]
    public async Task BroadcastProfilesManager_Dispatches_Arbitrary_PlatformAppliers_And_Evaluates_Combined_Sync()
    {
        var vkApplier = Substitute.For<IPlatformBroadcastProfileApplier>();
        vkApplier.Platform.Returns(StreamingPlatform.VkLive);
        vkApplier.ApplyAsync(Arg.Any<BroadcastProfile>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastProfilePlatformApplyResult(StreamingPlatform.VkLive, true));
        vkApplier.EvaluateSync(Arg.Any<BroadcastProfile>(), Arg.Any<StreamInfo?>())
            .Returns(call =>
            {
                var stream = call.Arg<StreamInfo?>();
                if (stream is null)
                {
                    return new(StreamingPlatform.VkLive, BroadcastProfileSyncState.Pending);
                }

                return string.Equals(stream.Title, "Общий стрим #3", StringComparison.Ordinal)
                    ? new(StreamingPlatform.VkLive, BroadcastProfileSyncState.Synced)
                    : new(StreamingPlatform.VkLive, BroadcastProfileSyncState.Drifted);
            });

        var twitchApplier = Substitute.For<IPlatformBroadcastProfileApplier>();
        twitchApplier.Platform.Returns(StreamingPlatform.Twitch);
        twitchApplier.ApplyAsync(Arg.Any<BroadcastProfile>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastProfilePlatformApplyResult(StreamingPlatform.Twitch, true));
        twitchApplier.EvaluateSync(Arg.Any<BroadcastProfile>(), Arg.Any<StreamInfo?>())
            .Returns(new BroadcastProfileSyncEvaluation(StreamingPlatform.Twitch, BroadcastProfileSyncState.Synced));

        var eventBus = Substitute.For<IEventBus>();
        BroadcastProfileApplied? applied = null;
        eventBus.PublishAsync(Arg.Do<BroadcastProfileApplied>(e => applied = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var vkPlatform = new StubPlatform(StreamingPlatform.VkLive, "VK Live");
        var twitchPlatform = new StubPlatform(StreamingPlatform.Twitch, "Twitch")
        {
            Stream = new() { Title = "Общий стрим #3", GameId = "1", ViewerCount = 2 },
        };
        var registry = new ChatPlatformRegistry([twitchPlatform, vkPlatform]);

        var manager = new BroadcastProfilesManager(
            _profilesStore,
            applier: null,
            eventBus,
            new TestTimeProvider(),
            NullLogger<BroadcastProfilesManager>.Instance,
            _platformsStore,
            _youtubeStore,
            youtubeApplier: null,
            platformAppliers: [vkApplier, twitchApplier],
            platformRegistry: registry);

        var profile = new BroadcastProfile
        {
            Name = "Трёхплатформенный",
            Title = "Общий стрим #{n}",
            CurrentNumber = 3,
        };
        manager.Upsert(profile, [StreamingPlatform.Twitch, StreamingPlatform.VkLive]);

        await manager.ApplyAsync(profile.Id, CancellationToken.None);

        var pendingSummary = manager.EvaluateSync(profile, registry);

        vkPlatform.Stream = new() { Title = "Устарело", ViewerCount = 1 };
        var driftedSummary = manager.EvaluateSync(profile, registry);

        vkPlatform.Stream = new() { Title = "Общий стрим #3", ViewerCount = 1 };
        var syncedSummary = manager.EvaluateSync(profile, registry);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.Not.Null);
            Assert.That(applied!.PlatformResults, Has.Count.EqualTo(2));
            Assert.That(applied.PlatformResults!.Select(r => r.Platform), Is.EquivalentTo([StreamingPlatform.Twitch, StreamingPlatform.VkLive]));

            Assert.That(pendingSummary.State, Is.EqualTo(BroadcastProfileSyncState.Pending));
            Assert.That(pendingSummary.PendingPlatforms, Is.EqualTo(new[] { StreamingPlatform.VkLive }));

            Assert.That(driftedSummary.State, Is.EqualTo(BroadcastProfileSyncState.Drifted));
            Assert.That(driftedSummary.DriftedPlatforms, Is.EqualTo(new[] { StreamingPlatform.VkLive }));

            Assert.That(syncedSummary.State, Is.EqualTo(BroadcastProfileSyncState.Synced));
            Assert.That(syncedSummary.DriftedPlatforms, Is.Empty);
            Assert.That(syncedSummary.PendingPlatforms, Is.Empty);
        });

        await vkApplier.Received(1).ApplyAsync(
            Arg.Is<BroadcastProfile>(p => p.Id == profile.Id),
            "Общий стрим #3",
            Arg.Any<CancellationToken>());
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamInfo? Stream { get; set; }
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus => new MutableStreamStatus(() => Stream);
        public IPlatformMessenger Messenger => null!;
    }

    private sealed class MutableStreamStatus(Func<StreamInfo?> streamAccessor) : IStreamStatus
    {
        public StreamStatus CurrentStatus => streamAccessor() is null ? StreamStatus.Offline : StreamStatus.Online;
        public StreamInfo? CurrentStream => streamAccessor();
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, CurrentStream, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
