using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class BotLifecycleAutomationSectionTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("bot-lifecycle-section");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    [Test]
    public async Task LoadEditSave_SharesSingleDraftBetweenSummaryAndPlatformTabs_AndSyncsTwitch()
    {
        var settingsPath = Path.Combine(_directory.FullName, "settings.json");
        var lifecyclePath = Path.Combine(_directory.FullName, "platform-lifecycle.json");
        var pollsPath = Path.Combine(_directory.FullName, "polls.json");

        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath, gate);
        settingsManager.Mutate(s =>
        {
            s.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline = true;
            s.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline = false;
        });

        var lifecycleStore = new PlatformLifecycleSettingsStore(settingsManager, null, lifecyclePath, gate);
        var registry = new ChatPlatformRegistry(
        [
            new StubPlatform(StreamingPlatform.Twitch, "Twitch"),
            new StubPlatform(StreamingPlatform.YouTube, "YouTube"),
            new StubPlatform(StreamingPlatform.VkLive, "VK Видео Live"),
        ]);

        var lifecycleVm = new BotLifecycleAutomationSectionViewModel(registry);
        var contributor = new StreamSettingsSectionContributor(
            new(),
            lifecycleVm,
            new(new(filePath: pollsPath), TimeProvider.System),
            lifecycleStore,
            settingsManager);

        var catalogStore = new ChannelCatalogStore(filePath: Path.Combine(_directory.FullName, "channels.json"), gate: gate);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var server = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
        using var settingsPage = new SettingsPageViewModel(
            [
                contributor,
                new ChannelsSettingsSectionContributor(
                    new(catalogStore, platformRegistry: registry),
                    new(catalogStore, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance)),
                    catalogStore),
            ],
            settingsManager,
            gate,
            server,
            bus,
            NullLogger<SettingsPageViewModel>.Instance,
            dialogs,
            new MemorySettings(),
            platformRegistry: registry);

        await using var connection = new BotConnectionManager(
            new AppHost([], NullLogger<AppHost>.Instance),
            [],
            null,
            bus,
            NullLogger<BotConnectionManager>.Instance);
        using var multistreamStatus = new MultistreamTileViewModel(
            registry,
            catalogStore,
            connection,
            new Core.Platforms.Auth.PlatformAuthRegistry([]),
            [],
            bus,
            new ManualUiDispatcher(),
            new YouTubeSettingsStore(filePath: Path.Combine(_directory.FullName, "youtube.json"), gate: gate));
        var multistream = new MultistreamPageViewModel(settingsPage, multistreamStatus, []);

        var summaryBlock = multistream.LifecycleAndBroadcastBlocks.Single(b => b.Key == "stream/lifecycle");
        var summaryVm = (BotLifecycleAutomationSectionViewModel)summaryBlock.Content;

        multistream.OpenPlatform(StreamingPlatform.YouTube);
        var youtubeBlock = multistream.PlatformSettings.Single(b => b.Key == "stream/lifecycle/youtube");
        var youtubePlatformItem = (PlatformLifecycleItemViewModel)youtubeBlock.Content;
        var summaryYouTubeItem = summaryVm.Platforms.Single(p => p.Platform == StreamingPlatform.YouTube);
        var summaryTwitchItem = summaryVm.Platforms.Single(p => p.Platform == StreamingPlatform.Twitch);
        var summaryVkItem = summaryVm.Platforms.Single(p => p.Platform == StreamingPlatform.VkLive);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summaryVm.Platforms, Has.Count.EqualTo(3));
            Assert.That(youtubePlatformItem, Is.SameAs(summaryYouTubeItem));
            Assert.That(summaryTwitchItem.AutoConnectOnStreamOnline, Is.True);
            Assert.That(summaryTwitchItem.AutoDisconnectOnStreamOffline, Is.False);
            Assert.That(youtubePlatformItem.AutoConnectOnStreamOnline, Is.False);
            Assert.That(settingsPage.HasChanges, Is.False);
        }

        youtubePlatformItem.AutoConnectOnStreamOnline = true;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summaryYouTubeItem.AutoConnectOnStreamOnline, Is.True);
            Assert.That(settingsPage.HasChanges, Is.True);
        }

        summaryTwitchItem.AutoConnectOnStreamOnline = false;
        summaryTwitchItem.AutoDisconnectOnStreamOffline = true;
        summaryVkItem.AutoConnectOnStreamOnline = true;

        await settingsPage.SaveCommand.ExecuteAsync(null);

        var reloaded = new PlatformLifecycleSettingsStore(settingsManager, null, lifecyclePath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settingsPage.HasChanges, Is.False);
            Assert.That(File.Exists(lifecyclePath), Is.True);
            Assert.That(reloaded.Get(StreamingPlatform.Twitch).AutoConnectOnStreamOnline, Is.False);
            Assert.That(reloaded.Get(StreamingPlatform.Twitch).AutoDisconnectOnStreamOffline, Is.True);
            Assert.That(reloaded.Get(StreamingPlatform.YouTube).AutoConnectOnStreamOnline, Is.True);
            Assert.That(reloaded.Get(StreamingPlatform.VkLive).AutoConnectOnStreamOnline, Is.True);
            Assert.That(settingsManager.Current.Twitch.BotLifecycleAutomation.AutoConnectOnStreamOnline, Is.False);
            Assert.That(settingsManager.Current.Twitch.BotLifecycleAutomation.AutoDisconnectOnStreamOffline, Is.True);
        }

        summaryVkItem.AutoDisconnectOnStreamOffline = true;
        Assert.That(settingsPage.HasChanges, Is.True);

        settingsPage.DiscardUnsavedChanges();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settingsPage.HasChanges, Is.False);
            Assert.That(summaryVkItem.AutoDisconnectOnStreamOffline, Is.False);
            Assert.That(youtubePlatformItem.AutoConnectOnStreamOnline, Is.True);
        }
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamingPlatform Id => id;

        public string DisplayName => displayName;

        public PlatformCapabilities Capabilities => PlatformCapabilities.None;

        public bool IsEnabled => true;

        public bool IsConnected => false;

        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();

        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Unknown;

        public StreamInfo? CurrentStream => null;

        public StreamStatusSnapshot GetSnapshot() => new(StreamStatus.Unknown, null, null);

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod is not null
                && targetMethod.ReturnType != typeof(void)
                && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
