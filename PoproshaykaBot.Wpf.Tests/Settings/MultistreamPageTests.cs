using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class MultistreamPageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Registered_platform_uses_registry_name_and_catalog_without_optional_controls(bool includeTwitch)
    {
        var data = Directory.CreateTempSubdirectory("poproshayka-multistream-page-");
        try
        {
            var platform = StreamingPlatform.VkLive;
            var additional = new FakePlatform(platform, "VK Видео Live");
            IChatPlatform[] registeredPlatforms = includeTwitch ? [new FakePlatform(StreamingPlatform.Twitch, "Twitch"), additional] : [additional];
            var registry = new ChatPlatformRegistry(registeredPlatforms);
            var gate = new SettingsWriteGate();
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(data.FullName, "channels.json"), gate: gate);
            Assert.That(catalog.Save(new()
            {
                Channels =
                [
                    new() { Platform = platform, ChannelId = "one", DisplayName = "Первый", IsConfigured = true },
                    new() { Platform = platform, ChannelId = "two", DisplayName = "Активный", IsConfigured = true },
                ],
                Platforms = [new() { Platform = platform, ActiveChannelId = "two" }],
            }), Is.True);
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(data.FullName, "settings.json"), gate);
            var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var editor = new ChannelCatalogSettingsSectionViewModel(catalog);
            var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));
            using var settings = new SettingsPageViewModel([new ChannelsSettingsSectionContributor(editor, groups, catalog)],
                manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance,
                DispatchProxy.Create<IDialogService, NoDialog>(), new MemorySettings(), platformRegistry: registry);
            await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [],
                null, bus, NullLogger<BotConnectionManager>.Instance);
            using var status = new MultistreamTileViewModel(registry, catalog, connection, new PlatformAuthRegistry([]),
                [], bus, new ManualUiDispatcher(), new YouTubeSettingsStore(filePath: Path.Combine(data.FullName, "youtube.json")));
            var youTubeControls = new YouTubeMultistreamPlatformControls(settings);
            var page = new MultistreamPageViewModel(settings, status, [youTubeControls]);
            Assert.That(page.SelectedPlatform, Is.EqualTo(registeredPlatforms[0].Id));

            page.OpenPlatform(platform);
            page.OpenPlatform(StreamingPlatform.YouTube);

            Assert.Multiple(() =>
            {
                Assert.That(page.Cards.Select(card => card.Status.Platform), Is.EqualTo(registeredPlatforms.Select(platform => platform.Id)));
                Assert.That(youTubeControls.Settings, Is.Null);
                Assert.That(page.Cards.Single(card => card.Status.Platform == platform).Controls, Is.Null);
                Assert.That(page.SelectedPlatform, Is.EqualTo(platform));
                Assert.That(page.EditorTitle, Is.EqualTo("Каналы VK Видео Live"));
                Assert.That(page.HasPlatformSettings, Is.False);
                Assert.That(editor.SelectedPlatform, Is.EqualTo(platform));
                Assert.That(editor.SelectedChannel?.ChannelId, Is.EqualTo("two"));
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Channels));
            });

            page.OpenPlatformFromOverview(platform);
            Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Channels));
            page.OpenAutomation();
            Assert.Multiple(() =>
            {
                Assert.That(page.IsAutomationExpanded, Is.True);
                Assert.That(page.TakeSectionRequest(), Is.EqualTo(MultistreamSection.Automation));
            });
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    public class NoDialog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }

    private sealed class FakePlatform(StreamingPlatform id, string displayName) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus { get; } = new FakeStreamStatus();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

    private sealed class FakeStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, null, null);
        public Task RefreshLiveSnapshotAsync() => throw new NotSupportedException();
    }
}
