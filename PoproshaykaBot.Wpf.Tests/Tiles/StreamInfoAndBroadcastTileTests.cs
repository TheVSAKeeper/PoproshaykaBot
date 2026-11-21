using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class StreamInfoAndBroadcastTileTests
{
    [Test]
    public async Task StreamInfoTile_показывает_вкладки_площадок_суммарных_зрителей_и_переключает_в_один_клик()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var twitchInfo = new StreamInfo
        {
            Title = "Twitch эфир",
            GameName = "Factorio",
            ViewerCount = 61,
            StartedAt = DateTime.UtcNow.AddMinutes(-20),
            UserLogin = "streamer",
        };
        var youtubeInfo = new StreamInfo
        {
            Title = "YouTube эфир",
            ViewerCount = 124,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            UserLogin = "yt",
        };

        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, false, StreamStatus.Offline, null, "streamer");
        var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, false, StreamStatus.Online, youtubeInfo, "UC1");
        var registry = new StubPlatformRegistry(twitch, youtube);

        using var tile = new StreamInfoTileViewModel(
            registry,
            bus,
            NullLogger<StreamInfoTileViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoOpLauncher());

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMultiplePlatforms, Is.True);
            Assert.That(tile.HeaderActions.Any(static a => a.ToolTip == "Сменить площадку в информации о стриме"), Is.True);
            Assert.That(tile.PlatformTabs.Select(static t => t.Platform),
                Is.EqualTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }));
            Assert.That(tile.PlatformTabs[0].IsSelected, Is.False);
            Assert.That(tile.PlatformTabs[0].IsOnline, Is.False);
            Assert.That(tile.PlatformTabs[0].ViewerCountText, Is.EqualTo("Офлайн"));
            Assert.That(tile.PlatformTabs[0].StatusSeverity, Is.EqualTo(StatusSeverity.None));
            Assert.That(tile.PlatformTabs[1].IsSelected, Is.True);
            Assert.That(tile.PlatformTabs[1].IsOnline, Is.True);
            Assert.That(tile.PlatformTabs[1].ViewerCountText, Is.EqualTo("124"));
            Assert.That(tile.PlatformTabs[1].StatusSeverity, Is.EqualTo(StatusSeverity.Success));
            Assert.That(tile.HasCombinedLiveSummary, Is.False);
            Assert.That(tile.TotalLiveViewersText, Is.Empty);
        });

        twitch.MutableStream.Status = StreamStatus.Online;
        twitch.MutableStream.Info = twitchInfo;
        await bus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "streamer", twitchInfo));

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasCombinedLiveSummary, Is.True);
            Assert.That(tile.TotalLiveViewersText, Is.EqualTo("Всего зрителей: 185"));
            Assert.That(tile.PlatformTabs[0].IsOnline, Is.True);
            Assert.That(tile.PlatformTabs[0].ViewerCountText, Is.EqualTo("61"));
            Assert.That(tile.PlatformTabs[0].StatusSeverity, Is.EqualTo(StatusSeverity.Success));
        });

        tile.PlatformTabs[0].SelectCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(tile.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(tile.PlatformTabs[0].IsSelected, Is.True);
            Assert.That(tile.PlatformTabs[1].IsSelected, Is.False);
            Assert.That(tile.StreamTitle, Is.EqualTo("Twitch эфир"));
            Assert.That(tile.DetailLabel, Is.EqualTo("Игра"));
            Assert.That(tile.DetailValue, Is.EqualTo("Factorio"));
            Assert.That(tile.ViewerCount, Is.EqualTo(61));
        });
    }

    [Test]
    public async Task BroadcastStatusTile_формирует_чипы_состояний_площадок_и_сохраняет_контракты()
    {
        var directory = Directory.CreateTempSubdirectory("poproshayka-broadcast-chips-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(directory.FullName, "settings.json"));
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(directory.FullName, "channels.json"));
            Assert.That(catalog.Mutate(s =>
            {
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-1", DisplayName = "Основной", IsConfigured = true });
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-2", DisplayName = "Запасной", IsConfigured = true });
            }), Is.True);

            var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, true, StreamStatus.Online, null, "streamer");
            var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, false, StreamStatus.Online, null, "Основной");
            var vk = new StubPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, false, false, StreamStatus.Offline, null, "vk");
            var registry = new StubPlatformRegistry(twitch, youtube, vk);

            var targets = new MutableTargetsProvider(new HashSet<StreamingPlatform>
            {
                StreamingPlatform.Twitch,
                StreamingPlatform.YouTube,
                StreamingPlatform.VkLive,
            });
            var state = new StubBroadcastState
            {
                LiveSet = [StreamingPlatform.Twitch, StreamingPlatform.YouTube],
            };
            await using var scheduler = new BroadcastScheduler(
                new InertBroadcastMessenger(),
                targets,
                settings,
                bus,
                TimeProvider.System,
                NullLogger<BroadcastScheduler>.Instance);

            using var tile = new BroadcastStatusTileViewModel(
                scheduler,
                settings,
                state,
                registry,
                targets,
                bus,
                new InertDialogService(),
                uiDispatcher: new ManualUiDispatcher(),
                catalogStore: catalog);

            Assert.Multiple(() =>
            {
                Assert.That(tile.PlatformsText, Is.EqualTo("Twitch, YouTube (Основной), VK Видео Live"));
                Assert.That(tile.HasTargetPlatformChips, Is.True);
                Assert.That(tile.TargetPlatformChips, Has.Count.EqualTo(3));
                Assert.That(tile.HeaderActions[0].ToolTip, Is.EqualTo("Запустить рассылку"));
                Assert.That(tile.HeaderActions[1].ToolTip, Is.EqualTo("Остановить рассылку"));

                Assert.That(tile.TargetPlatformChips[0].Platform, Is.EqualTo(StreamingPlatform.Twitch));
                Assert.That(tile.TargetPlatformChips[0].DisplayName, Is.EqualTo("Twitch"));
                Assert.That(tile.TargetPlatformChips[0].StateLabel, Is.EqualTo("В эфире"));
                Assert.That(tile.TargetPlatformChips[0].Severity, Is.EqualTo(StatusSeverity.Success));

                Assert.That(tile.TargetPlatformChips[1].Platform, Is.EqualTo(StreamingPlatform.YouTube));
                Assert.That(tile.TargetPlatformChips[1].DisplayName, Is.EqualTo("YouTube (Основной)"));
                Assert.That(tile.TargetPlatformChips[1].StateLabel, Is.EqualTo("В эфире"));
                Assert.That(tile.TargetPlatformChips[1].Severity, Is.EqualTo(StatusSeverity.Warning));

                Assert.That(tile.TargetPlatformChips[2].Platform, Is.EqualTo(StreamingPlatform.VkLive));
                Assert.That(tile.TargetPlatformChips[2].StateLabel, Is.EqualTo("Выключена"));
                Assert.That(tile.TargetPlatformChips[2].Severity, Is.EqualTo(StatusSeverity.Warning));
            });

            twitch.MutableStream.Status = StreamStatus.Offline;
            youtube.MutableStream.Status = StreamStatus.Offline;
            state.LiveSet.Clear();
            vk.IsEnabledValue = true;
            await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Connected));

            Assert.Multiple(() =>
            {
                Assert.That(tile.TargetPlatformChips[0].StateLabel, Is.EqualTo("Подключён"));
                Assert.That(tile.TargetPlatformChips[0].Severity, Is.EqualTo(StatusSeverity.Success));

                Assert.That(tile.TargetPlatformChips[1].StateLabel, Is.EqualTo("Офлайн"));
                Assert.That(tile.TargetPlatformChips[1].Severity, Is.EqualTo(StatusSeverity.None));

                Assert.That(tile.TargetPlatformChips[2].StateLabel, Is.EqualTo("Офлайн"));
                Assert.That(tile.TargetPlatformChips[2].Severity, Is.EqualTo(StatusSeverity.None));
            });

            targets.Targets = new HashSet<StreamingPlatform>();
            await bus.PublishAsync(new PlatformConnectionChanged(StreamingPlatform.Twitch, BotLifecyclePhase.Disconnected));

            Assert.Multiple(() =>
            {
                Assert.That(tile.PlatformsText, Is.EqualTo("–"));
                Assert.That(tile.HasTargetPlatformChips, Is.False);
                Assert.That(tile.TargetPlatformChips, Is.Empty);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class NoOpLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class InertBroadcastMessenger : IBroadcastMessenger
    {
        private static readonly BroadcastSendResult EmptyResult = new(new Dictionary<StreamingPlatform, ChatEnqueueResult>().ToFrozenDictionary());

        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
            => ChatEnqueueResult.Queued;

        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;

        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;

        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;
    }

    private sealed class MutableTargetsProvider(IReadOnlySet<StreamingPlatform> targets) : IBroadcastPlatformTargetsProvider
    {
        public IReadOnlySet<StreamingPlatform> Targets { get; set; } = targets;

        public IReadOnlySet<StreamingPlatform> GetTargetPlatforms() => Targets;
    }

    private sealed class StubBroadcastState : IBroadcastState
    {
        public HashSet<StreamingPlatform> LiveSet { get; init; } = [];

        public bool IsAnyLive => LiveSet.Count > 0;

        public Guid? CurrentBroadcastId => null;

        public IReadOnlySet<StreamingPlatform> LivePlatforms => LiveSet;

        public bool IsPlatformLive(StreamingPlatform platform) => LiveSet.Contains(platform);
    }

    private sealed class InertDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(true);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(true);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) { }

        public void Warning(string title, string message) { }

        public void Error(string title, string message) { }
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;

        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = platforms.FirstOrDefault(p => p.Id == platform);
            return chatPlatform is not null;
        }

        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);

        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();

        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(static p => p.IsEnabled).ToArray();

        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(static p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform(
        StreamingPlatform id,
        string displayName,
        PlatformCapabilities capabilities,
        bool isEnabled,
        bool isConnected,
        StreamStatus status,
        StreamInfo? info,
        string? configuredChannel) : IChatPlatform
    {
        public StreamingPlatform Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public PlatformCapabilities Capabilities { get; } = capabilities;

        public bool IsEnabledValue { get; set; } = isEnabled;

        public bool IsEnabled => IsEnabledValue;

        public bool IsConnected => isConnected;

        public MutableStreamStatus MutableStream { get; } = new(status, info);

        public IStreamStatus StreamStatus => MutableStream;

        public IPlatformMessenger Messenger => null!;

        public string? ConfiguredChannel { get; } = configuredChannel;
    }

    private sealed class MutableStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus Status { get; set; } = status;

        public StreamInfo? Info { get; set; } = info;

        public StreamStatus CurrentStatus => Status;

        public StreamInfo? CurrentStream => Info;

        public StreamStatusSnapshot GetSnapshot() => new(Status, Info, null);

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
