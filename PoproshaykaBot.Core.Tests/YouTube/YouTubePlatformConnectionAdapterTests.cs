using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube;

[TestFixture]
public class YouTubePlatformConnectionAdapterTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-conn-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
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
    public async Task Legacy_Foreign_Channel_Binds_Session_To_Resolved_Target_Not_Broadcaster()
    {
        var settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", Channel = "@foreign" });

        var accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "Bot" },
            new YouTubeAccountSettings { ChannelId = "UC_OWN", ChannelTitle = "Owner" });

        var broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        broadcastClient.ResolveChannelIdByHandleAsync("@foreign", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns("UC_FOREIGN");

        var resolver = new YouTubeTargetChannelResolver(broadcastClient, accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        var authService = new StubBotAuthService();

        var sessionContext = new YouTubeSessionContext();
        var channelRegistry = new PlatformChannelRegistry();
        var targetChannelProvider = Substitute.For<ITargetChannelProvider>();
        targetChannelProvider.Current.Returns(new TargetChannelState(string.Empty, string.Empty, IsDebugSession: false, IsSendingAllowed: true));
        var host = new PlatformHost(StreamingPlatform.YouTube, [], NullLogger<PlatformHost>.Instance);

        var adapter = new YouTubePlatformConnectionAdapter(
            authService,
            settingsStore,
            accountsStore,
            sessionContext,
            channelRegistry,
            targetChannelProvider,
            host,
            NullLogger<YouTubePlatformConnectionAdapter>.Instance,
            channelCatalogStore: null,
            channelResolver: resolver);

        var label = await adapter.BeginSessionAsync(new Progress<string>(_ => { }), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(label, Is.EqualTo("@foreign"));
            Assert.That(sessionContext.Current?.TargetChannelId, Is.EqualTo("UC_FOREIGN"));
            Assert.That(channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key.ChannelId, Is.EqualTo("UC_FOREIGN"));
        });
    }

    [Test]
    public void Legacy_Unresolved_Handle_Throws_Instead_Of_Opening_Unrestricted_Session()
    {
        var settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube-unresolved.json"));
        settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", Channel = "@missing" });

        var accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts-unresolved.json"));
        accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings { ChannelId = "UC_BOT", ChannelTitle = "Bot" },
            new YouTubeAccountSettings { ChannelId = "UC_OWN", ChannelTitle = "Owner" });

        var broadcastClient = Substitute.For<IYouTubeLiveBroadcastClient>();
        broadcastClient.ResolveChannelIdByHandleAsync("@missing", PlatformAuthRole.Bot, AuthContextIds.Default, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var resolver = new YouTubeTargetChannelResolver(broadcastClient, accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
        var authService = new StubBotAuthService();
        var sessionContext = new YouTubeSessionContext();
        var channelRegistry = new PlatformChannelRegistry();
        var targetChannelProvider = Substitute.For<ITargetChannelProvider>();
        targetChannelProvider.Current.Returns(new TargetChannelState(string.Empty, string.Empty, IsDebugSession: false, IsSendingAllowed: true));
        var host = new PlatformHost(StreamingPlatform.YouTube, [], NullLogger<PlatformHost>.Instance);

        var adapter = new YouTubePlatformConnectionAdapter(
            authService,
            settingsStore,
            accountsStore,
            sessionContext,
            channelRegistry,
            targetChannelProvider,
            host,
            NullLogger<YouTubePlatformConnectionAdapter>.Instance,
            channelCatalogStore: null,
            channelResolver: resolver);

        Assert.ThrowsAsync<InvalidOperationException>(() => adapter.BeginSessionAsync(new Progress<string>(_ => { }), CancellationToken.None));
        Assert.That(sessionContext.Current, Is.Null);
    }

    private sealed class StubBotAuthService : IPlatformAuthService
    {
        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => role == PlatformAuthRole.Bot;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>(role == PlatformAuthRole.Bot ? "bot-token" : null);

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken) => false;

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }
}
