using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Twitch;

[TestFixture]
public sealed class TwitchPlatformEnablementTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("twitch-enablement-tests");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public void TwitchSettings_Enabled_DefaultsToTrue_AndPreservesCompatibilityWithoutField()
    {
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        File.WriteAllText(settingsPath, """{"twitch":{"channel":"legacy_channel"}}""");

        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new TwitchSettings().Enabled, Is.True);
            Assert.That(manager.Current.Twitch.Enabled, Is.True);
            Assert.That(manager.Current.Twitch.Channel, Is.EqualTo("legacy_channel"));
        }

        manager.Mutate(s => s.Twitch.Enabled = false);
        var reloaded = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        Assert.That(reloaded.Current.Twitch.Enabled, Is.False);
    }

    [Test]
    public void TwitchPlatformEnablementSource_RaisesChangedOnlyWhenEnabledChanges()
    {
        var manager = CreateSettingsManager();
        using var source = new TwitchPlatformEnablementSource(manager);
        var changes = 0;
        source.Changed += () => changes++;

        Assert.That(source.Platform, Is.EqualTo(StreamingPlatform.Twitch));
        Assert.That(source.IsEnabledInSettings, Is.True);

        manager.Mutate(s => s.Twitch.Channel = "other");
        Assert.That(changes, Is.EqualTo(0));

        manager.Mutate(s => s.Twitch.Enabled = false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(source.IsEnabledInSettings, Is.False);
        }

        manager.Mutate(s => s.Twitch.Channel = "another");
        Assert.That(changes, Is.EqualTo(1));

        manager.SaveSettings(new AppSettings { Twitch = new TwitchSettings { Enabled = true } });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(source.IsEnabledInSettings, Is.True);
        }
    }

    [Test]
    public void TwitchServiceCollection_RegistersTwitchPlatformEnablementSource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<ITargetChannelProvider>());
        services.AddSettingsStores();
        services.AddTwitchClients();

        using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IPlatformEnablementSource>().ToArray();

        Assert.That(sources.Any(s => s.Platform == StreamingPlatform.Twitch), Is.True);
    }

    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void TwitchPlatformConnectionAdapter_IsEnabled_RespectsSettingsAndDebugSession(
        bool twitchEnabled,
        bool isDebugSession,
        bool expected)
    {
        var manager = CreateSettingsManager();
        manager.Mutate(s => s.Twitch.Enabled = twitchEnabled);

        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("ch", "ch", isDebugSession, true));

        var host = new PlatformHost(StreamingPlatform.Twitch, [], NullLogger<PlatformHost>.Instance);
        var adapter = new TwitchPlatformConnectionAdapter(
            Substitute.For<ITwitchOAuthService>(),
            targetChannel,
            host,
            NullLogger<TwitchPlatformConnectionAdapter>.Instance,
            settingsManager: manager);

        Assert.That(adapter.IsEnabled, Is.EqualTo(expected));
    }

    [TestCase(true, false, true)]
    [TestCase(false, false, false)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void TwitchChatPlatform_IsEnabled_RespectsSettingsAndDebugSession(
        bool twitchEnabled,
        bool isDebugSession,
        bool expected)
    {
        var manager = CreateSettingsManager();
        manager.Mutate(s => s.Twitch.Enabled = twitchEnabled);

        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("ch", "ch", isDebugSession, true));

        var accounts = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts.json"));
        var channels = new PlatformChannelRegistry();
        var decorations = new ChatDecorationsProvider(Substitute.For<ITwitchHelixClient>());
        var sender = new ChatSender(
            Substitute.For<ITwitchHelixClient>(),
            Substitute.For<IBroadcasterIdProvider>(),
            Substitute.For<IBotUserIdProvider>(),
            targetChannel,
            new(TimeProvider.System),
            NullLogger<ChatSender>.Instance);
        var messenger = new TwitchChatMessenger(sender, accounts, NullLogger<TwitchChatMessenger>.Instance);

        var platform = new TwitchChatPlatform(
            Substitute.For<IStreamStatus>(),
            messenger,
            decorations,
            channels,
            accounts,
            NullLogger<TwitchChatPlatform>.Instance,
            targetChannel,
            settingsManager: manager);

        Assert.That(platform.IsEnabled, Is.EqualTo(expected));
    }

    [Test]
    public async Task EventSubConnectionHost_SkipsStartWhenTwitchDisabled_AndReactsToEnablementToggle()
    {
        var manager = CreateSettingsManager();
        manager.Mutate(s => s.Twitch.Enabled = false);

        var accounts = new AccountsStore(filePath: Path.Combine(_directory.FullName, "eventsub-accounts.json"));
        accounts.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.AccessToken = "bot-token";
            account.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
            account.Scopes = [.. TwitchScopes.BotRequired];
        });

        var eventSubClient = Substitute.For<ITwitchEventSubClient>();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("ch", "ch", IsDebugSession: false, IsSendingAllowed: true));

        await using var host = new EventSubConnectionHost(
            PlatformAuthRole.Bot,
            eventSubClient,
            accounts,
            bus,
            NullLogger<EventSubConnectionHost>.Instance,
            settingsManager: manager,
            targetChannelProvider: targetChannel);

        await host.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);
        await eventSubClient.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());

        manager.Mutate(s => s.Twitch.Enabled = true);
        await WaitUntilAsync(() => eventSubClient.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(ITwitchEventSubClient.StartAsync)));
        await eventSubClient.Received(1).StartAsync(Arg.Any<CancellationToken>());

        manager.Mutate(s => s.Twitch.Enabled = false);
        await WaitUntilAsync(() => eventSubClient.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(ITwitchEventSubClient.StopAsync)));
        await eventSubClient.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    private SettingsManager CreateSettingsManager() =>
        new(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }
}
