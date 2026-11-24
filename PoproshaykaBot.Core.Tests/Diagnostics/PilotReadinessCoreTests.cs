using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Tests.Diagnostics;

[TestFixture]
public sealed class PilotReadinessCoreTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("pilot-readiness-core");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void Снимок_именованных_контекстов_Twitch_описывает_контексты_без_утечки_токенов()
    {
        var path = Path.Combine(_directory.FullName, "accounts.json");
        var store = new AccountsStore(null, path);
        store.Mutate(PlatformAuthRole.Bot, "alt-bot", account =>
        {
            account.Login = "mybot";
            account.UserId = "4242";
            account.AccessToken = "live-access-secret";
            account.RefreshToken = "live-refresh-secret";
            account.StoredScopes = ["user:read:chat"];
        });

        var description = store.DescribeContextsSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description, Does.Contain("alt-bot/Bot"));
            Assert.That(description, Does.Contain("логин mybot"));
            Assert.That(description, Does.Contain("userId 4242"));
            Assert.That(description, Does.Contain("access-токен есть"));
            Assert.That(description, Does.Not.Contain("live-access-secret"));
            Assert.That(description, Does.Not.Contain("live-refresh-secret"));
        }
    }

    [Test]
    public void Снимок_именованных_контекстов_YouTube_описывает_контексты_без_утечки_секретов()
    {
        var path = Path.Combine(_directory.FullName, "youtube-accounts.json");
        var store = new YouTubeAccountsStore(null, path);
        store.SaveClientSecret("yt-client-secret-raw");
        store.Mutate(PlatformAuthRole.Broadcaster, "yt-broadcaster-1", account =>
        {
            account.ChannelTitle = "MainYT";
            account.ChannelId = "UC_MAIN_1";
            account.AccessToken = "yt-access-secret";
            account.RefreshToken = "yt-refresh-secret";
            account.StoredScopes = ["https://www.googleapis.com/auth/youtube.readonly"];
        });

        var description = store.DescribeContextsSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description, Does.Contain("yt-broadcaster-1/Broadcaster"));
            Assert.That(description, Does.Contain("канал MainYT"));
            Assert.That(description, Does.Contain("channelId UC_MAIN_1"));
            Assert.That(description, Does.Contain("access-токен есть"));
            Assert.That(description, Does.Contain("refresh-токен есть"));
            Assert.That(description, Does.Not.Contain("yt-client-secret-raw"));
            Assert.That(description, Does.Not.Contain("yt-access-secret"));
            Assert.That(description, Does.Not.Contain("yt-refresh-secret"));
        }
    }

    [Test]
    public void Каталог_переноса_включает_настройки_и_статистику_команд_и_профили_YouTube()
    {
        var files = LegacyDataCatalog.AllEntries.Select(static e => e.FileName).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(files, Does.Contain(LegacyDataCatalog.CommandSettingsFileName));
            Assert.That(files, Does.Contain(LegacyDataCatalog.CommandUsageFileName));
            Assert.That(files, Does.Contain(LegacyDataCatalog.BroadcastProfileYouTubeFileName));
        }
    }

    [Test]
    public async Task Подключение_Twitch_дозаполняет_пустой_UserId_бота_из_старого_профиля()
    {
        var accountsPath = Path.Combine(_directory.FullName, "accounts.json");
        var channelsPath = Path.Combine(_directory.FullName, "channels.json");
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");

        var accountsStore = new AccountsStore(null, accountsPath);
        accountsStore.Mutate(PlatformAuthRole.Bot, AuthContextIds.Default, account =>
        {
            account.Login = "legacybot";
            account.UserId = string.Empty;
            account.AccessToken = "valid-legacy-token";
        });

        var catalogStore = new ChannelCatalogStore(null, channelsPath);
        catalogStore.Mutate(catalog =>
        {
            catalog.Channels.Add(new()
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "1001",
                DisplayName = "streamer",
                Handle = "streamer",
                IsConfigured = true,
                MonitoringEnabled = true,
                BroadcasterContextId = AuthContextIds.Default,
                BroadcasterVerifiedSubjectId = "1001",
                BotContextId = AuthContextIds.Default,
                BotVerifiedSubjectId = null,
            });
            catalog.Platforms.Add(new()
            {
                Platform = StreamingPlatform.Twitch,
                ActiveChannelId = "1001",
                SwitchMode = ChannelSwitchMode.Manual,
            });
        });

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        settingsManager.Mutate(s => s.Twitch.Channel = "streamer");

        var oauth = Substitute.For<ITwitchOAuthService>();
        oauth.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns("valid-legacy-token");
        oauth.ValidateAsync("valid-legacy-token", Arg.Any<CancellationToken>())
            .Returns(new TokenValidationInfo("legacybot", "777", "client-id", ["user:read:chat"], 3600));

        var targetProvider = new TargetChannelProvider(
            settingsManager,
            new DebugChannelStore(null, Path.Combine(_directory.FullName, "debug-channel.json")),
            DebugChannelOverride.None);
        var platformHost = new PlatformHost(StreamingPlatform.Twitch, [], NullLogger<PlatformHost>.Instance);
        var registry = new PlatformChannelRegistry();

        var adapter = new TwitchPlatformConnectionAdapter(
            oauth,
            targetProvider,
            platformHost,
            NullLogger<TwitchPlatformConnectionAdapter>.Instance,
            catalogStore,
            registry,
            chatIngestionService: null,
            accountsStore);

        var channel = await adapter.BeginSessionAsync(new Progress<string>(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(channel, Is.EqualTo("streamer"));
            Assert.That(accountsStore.Load(PlatformAuthRole.Bot).UserId, Is.EqualTo("777"));
            Assert.That(catalogStore.GetActiveChannel(StreamingPlatform.Twitch)?.BotVerifiedSubjectId, Is.EqualTo("777"));
        }
    }

    [Test]
    public async Task Подключение_Twitch_не_записывает_субъект_канала_если_аккаунт_бота_сменился_во_время_проверки()
    {
        var accountsPath = Path.Combine(_directory.FullName, "accounts-race.json");
        var channelsPath = Path.Combine(_directory.FullName, "channels-race.json");
        var settingsPath = Path.Combine(_directory.FullName, "settings-race.json");

        var accountsStore = new AccountsStore(null, accountsPath);
        accountsStore.Mutate(PlatformAuthRole.Bot, AuthContextIds.Default, account =>
        {
            account.Login = "legacybot";
            account.UserId = string.Empty;
            account.AccessToken = "token-before-swap";
        });

        var catalogStore = new ChannelCatalogStore(null, channelsPath);
        catalogStore.Mutate(catalog =>
        {
            catalog.Channels.Add(new()
            {
                Platform = StreamingPlatform.Twitch,
                ChannelId = "1001",
                DisplayName = "streamer",
                Handle = "streamer",
                IsConfigured = true,
                MonitoringEnabled = true,
                BroadcasterContextId = AuthContextIds.Default,
                BroadcasterVerifiedSubjectId = "1001",
                BotContextId = AuthContextIds.Default,
                BotVerifiedSubjectId = null,
            });
            catalog.Platforms.Add(new()
            {
                Platform = StreamingPlatform.Twitch,
                ActiveChannelId = "1001",
                SwitchMode = ChannelSwitchMode.Manual,
            });
        });

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        settingsManager.Mutate(s => s.Twitch.Channel = "streamer");

        var oauth = Substitute.For<ITwitchOAuthService>();
        oauth.GetAccessTokenAsync(PlatformAuthRole.Bot, Arg.Any<CancellationToken>())
            .Returns("token-before-swap");
        oauth.ValidateAsync("token-before-swap", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                accountsStore.Mutate(PlatformAuthRole.Bot, AuthContextIds.Default, account =>
                {
                    account.AccessToken = "token-after-swap";
                    account.UserId = "999";
                });
                return Task.FromResult<TokenValidationInfo?>(new TokenValidationInfo("legacybot", "777", "client-id", ["user:read:chat"], 3600));
            });

        var targetProvider = new TargetChannelProvider(
            settingsManager,
            new DebugChannelStore(null, Path.Combine(_directory.FullName, "debug-channel-race.json")),
            DebugChannelOverride.None);
        var platformHost = new PlatformHost(StreamingPlatform.Twitch, [], NullLogger<PlatformHost>.Instance);
        var registry = new PlatformChannelRegistry();

        var adapter = new TwitchPlatformConnectionAdapter(
            oauth,
            targetProvider,
            platformHost,
            NullLogger<TwitchPlatformConnectionAdapter>.Instance,
            catalogStore,
            registry,
            chatIngestionService: null,
            accountsStore);

        Assert.ThrowsAsync<InvalidOperationException>(() => adapter.BeginSessionAsync(new Progress<string>(), CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(accountsStore.Load(PlatformAuthRole.Bot).UserId, Is.EqualTo("999"));
            Assert.That(catalogStore.GetActiveChannel(StreamingPlatform.Twitch)?.BotVerifiedSubjectId, Is.Null);
        }
    }
}
