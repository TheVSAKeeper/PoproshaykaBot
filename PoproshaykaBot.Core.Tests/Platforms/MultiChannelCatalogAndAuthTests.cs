using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Debugging;

namespace PoproshaykaBot.Core.Tests.Platforms;

[TestFixture]
public sealed class MultiChannelCatalogAndAuthTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-mc-cat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void Catalog_persists_Twitch_and_YouTube_channels_named_contexts_and_keeps_backups_disabled_on_token_stores()
    {
        var gate = new SettingsWriteGate();
        var channelsPath = Path.Combine(_root, "channels.json");
        var twPath = Path.Combine(_root, "accounts.json");
        var ytPath = Path.Combine(_root, "youtube-accounts.json");

        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, channelsPath, gate);
        var twitchAccounts = new AccountsStore(NullLogger<AccountsStore>.Instance, twPath, gate);
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, ytPath, gate);

        twitchAccounts.SaveAll(
            new() { Login = "shared_bot", UserId = "bot-1", AccessToken = "tok-bot", RefreshToken = "ref-bot" },
            new() { Login = "caster_a", UserId = "tw-a", AccessToken = "tok-tw-a", RefreshToken = "ref-tw-a" });
        twitchAccounts.UpsertContext(
            PlatformAuthRole.Broadcaster,
            "caster-b",
            "Стример B",
            new() { Login = "caster_b", UserId = "tw-b", AccessToken = "tok-tw-b", RefreshToken = "ref-tw-b" });

        ytAccounts.SaveAll(
            "secret-1",
            new() { ChannelId = "UC_BOT", ChannelTitle = "YT Bot", AccessToken = "yt-bot", RefreshToken = "yt-bot-r" },
            new() { ChannelId = "UC_A", ChannelTitle = "YT Канал A", AccessToken = "yt-a", RefreshToken = "yt-a-r" });
        ytAccounts.UpsertContext(
            PlatformAuthRole.Broadcaster,
            "yt-ctx-b",
            "YT B",
            new() { ChannelId = "UC_B", ChannelTitle = "YT Канал B", AccessToken = "yt-b", RefreshToken = "yt-b-r" });
        ytAccounts.UpsertContext(
            PlatformAuthRole.Broadcaster,
            "yt-ctx-c",
            "YT C",
            new() { ChannelId = "UC_C", ChannelTitle = "YT Канал C", AccessToken = "yt-c", RefreshToken = "yt-c-r" });

        var backupFiles = Directory.GetFiles(_root, "*.bak", SearchOption.AllDirectories);
        Assert.That(backupFiles, Is.Empty);

        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "tw-a",
                    DisplayName = "caster_a",
                    Handle = "caster_a",
                    ChannelUrl = "https://twitch.tv/caster_a",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BroadcasterContextId = AuthContextIds.Default,
                    BroadcasterVerifiedSubjectId = "tw-a",
                    BotContextId = AuthContextIds.Default,
                    BotVerifiedSubjectId = "bot-1",
                },
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "tw-b",
                    DisplayName = "caster_b",
                    Handle = "caster_b",
                    ChannelUrl = "https://twitch.tv/caster_b",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BroadcasterContextId = "caster-b",
                    BroadcasterVerifiedSubjectId = "tw-b",
                    BotContextId = AuthContextIds.Default,
                    BotVerifiedSubjectId = "bot-1",
                },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_A",
                    DisplayName = "Канал A",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BroadcasterContextId = AuthContextIds.Default,
                    BotContextId = AuthContextIds.Default,
                },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_B",
                    DisplayName = "Канал B",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BroadcasterContextId = "yt-ctx-b",
                    BotContextId = AuthContextIds.Default,
                },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ChannelId = "UC_C",
                    DisplayName = "Канал C",
                    IsConfigured = true,
                    MonitoringEnabled = false,
                    BroadcasterContextId = "yt-ctx-c",
                    BotContextId = AuthContextIds.Default,
                },
            ];
            s.Platforms =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ActiveChannelId = "tw-a",
                    SwitchMode = ChannelSwitchMode.AutoDiscover,
                },
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = "UC_A",
                    SwitchMode = ChannelSwitchMode.PreselectedNext,
                    PreselectedNextChannelId = "UC_B",
                },
            ];
        });

        var reloadedCatalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, channelsPath, gate);
        var reloadedTwitch = new AccountsStore(NullLogger<AccountsStore>.Instance, twPath, gate);
        var reloadedYt = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, ytPath, gate);

        var channelsJson = File.ReadAllText(channelsPath);
        Assert.Multiple(() =>
        {
            Assert.That(channelsJson, Does.Not.Contain("tok-tw-a"));
            Assert.That(channelsJson, Does.Not.Contain("yt-b-r"));
            Assert.That(reloadedCatalog.GetConfiguredChannels(StreamingPlatform.Twitch), Has.Count.EqualTo(2));
            Assert.That(reloadedCatalog.GetMonitoredChannels(StreamingPlatform.YouTube).Select(c => c.ChannelId), Is.EqualTo(new[] { "UC_A", "UC_B" }));
            Assert.That(reloadedCatalog.GetPlatformSettings(StreamingPlatform.Twitch).SwitchMode, Is.EqualTo(ChannelSwitchMode.AutoDiscover));
            Assert.That(reloadedCatalog.GetPlatformSettings(StreamingPlatform.YouTube).PreselectedNextChannelId, Is.EqualTo("UC_B"));
            Assert.That(reloadedTwitch.Load(PlatformAuthRole.Broadcaster, "caster-b").UserId, Is.EqualTo("tw-b"));
            Assert.That(reloadedTwitch.Load(PlatformAuthRole.Bot, AuthContextIds.Default).UserId, Is.EqualTo("bot-1"));
            Assert.That(reloadedYt.Load(PlatformAuthRole.Broadcaster, "yt-ctx-c").ChannelId, Is.EqualTo("UC_C"));
            Assert.That(reloadedYt.ListContexts(PlatformAuthRole.Broadcaster), Has.Count.EqualTo(3));
        });
        var twContextRemoved = twitchAccounts.RemoveContext("caster-b");
        var twJsonAfterRemove = File.ReadAllText(twPath);
        Assert.Multiple(() =>
        {
            Assert.That(twContextRemoved, Is.True);
            Assert.That(twJsonAfterRemove, Does.Not.Contain("\"Contexts\""));
        });
    }

    [Test]
    public void Reconciler_retains_active_binding_across_startup_and_delayed_legacy_change_only_fills_empty_catalog()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_root, "channels.json"), gate);
        var twitchAccounts = new AccountsStore(NullLogger<AccountsStore>.Instance, Path.Combine(_root, "accounts.json"), gate);
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_root, "yt-accounts.json"), gate);
        var ytSettings = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_root, "yt.json"), gate);
        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null, null);
        settingsManager.Current.Returns(new AppSettings { Twitch = { Channel = "originA" } });

        twitchAccounts.SaveAll(
            new() { Login = "bot", UserId = "bot-id" },
            new() { Login = "originA", UserId = "tw-a" });

        var reconciler = new ChannelCatalogReconciler(
            catalog,
            settingsManager,
            twitchAccounts,
            ytSettings,
            ytAccounts,
            new FakeTargetChannelProvider("originA"),
            NullLogger<ChannelCatalogReconciler>.Instance);

        reconciler.ReconcileStartup();

        var twConfigured = catalog.GetConfiguredChannels(StreamingPlatform.Twitch);
        Assert.That(twConfigured, Has.Count.EqualTo(1));
        Assert.That(twConfigured[0].ChannelId, Is.EqualTo("tw-a"));

        catalog.Mutate(s => s.Channels.Add(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-b",
            DisplayName = "channelB",
            IsConfigured = true,
            MonitoringEnabled = true,
            BroadcasterContextId = "ctx-b",
            BroadcasterVerifiedSubjectId = "tw-b",
        }));

        reconciler.ReconcilePlatform(StreamingPlatform.Twitch);
        Assert.That(catalog.GetConfiguredChannels(StreamingPlatform.Twitch).Select(c => c.ChannelId),
            Is.EquivalentTo(new[] { "tw-a", "tw-b" }));
        Assert.That(twitchAccounts.Load(PlatformAuthRole.Broadcaster).Login, Is.EqualTo("originA"));
    }

    [Test]
    public void YouTubeChannelSeedProvider_sets_MonitoringEnabled_true_even_when_youtube_is_disabled()
    {
        var ytSettings = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_root, "yt-seed.json"));
        ytSettings.Save(new PoproshaykaBot.Core.YouTube.YouTubeSettings { Enabled = false, Channel = "UC0000000000000000000001" });
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_root, "yt-seed-acc.json"));

        var provider = new PoproshaykaBot.Core.YouTube.YouTubeChannelSeedProvider(ytSettings, ytAccounts);
        var seed = provider.TryCreateSeed();

        Assert.That(seed, Is.Not.Null);
        Assert.That(seed!.MonitoringEnabled, Is.True);
    }

    [Test]
    public void ChannelCatalogStore_Load_normalizes_duplicate_platforms_and_channels_from_disk()
    {
        var channelsPath = Path.Combine(_root, "channels-dup.json");
        File.WriteAllText(channelsPath, """
            {
              "channels": [
                { "platform": "Twitch", "channelId": " tw-1 ", "displayName": "OldName", "isConfigured": true },
                { "platform": "Twitch", "channelId": "tw-1", "displayName": "NewName", "monitoringEnabled": true }
              ],
              "platforms": [
                { "platform": "Twitch", "activeChannelId": "tw-old", "switchMode": "Manual" },
                { "platform": "Twitch", "activeChannelId": "tw-1", "switchMode": "AutoDiscover" }
              ]
            }
            """);

        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, channelsPath);
        var loaded = catalog.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Platforms, Has.Count.EqualTo(1));
            Assert.That(loaded.Platforms[0].ActiveChannelId, Is.EqualTo("tw-1"));
            Assert.That(loaded.Platforms[0].SwitchMode, Is.EqualTo(ChannelSwitchMode.AutoDiscover));
            Assert.That(() => loaded.Platforms.ToDictionary(static p => p.Platform), Throws.Nothing);

            Assert.That(loaded.Channels, Has.Count.EqualTo(1));
            Assert.That(loaded.Channels[0].ChannelId, Is.EqualTo("tw-1"));
            Assert.That(loaded.Channels[0].DisplayName, Is.EqualTo("NewName"));
            Assert.That(loaded.Channels[0].IsConfigured, Is.True);
            Assert.That(loaded.Channels[0].MonitoringEnabled, Is.True);
        });
    }
}
