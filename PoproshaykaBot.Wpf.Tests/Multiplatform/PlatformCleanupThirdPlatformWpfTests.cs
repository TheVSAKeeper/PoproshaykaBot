using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Diagnostics;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Diagnostics;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
public sealed class PlatformCleanupThirdPlatformWpfTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pb-1c-wpf-{Guid.NewGuid():N}");
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
    public void PlatformFilterOption_ForRegistry_includes_third_platform_and_custom_first_option()
    {
        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.None, true, StreamStatus.Offline, null, "tw"),
            new StubChatPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, true, StreamStatus.Offline, null, "vk"));

        var defaultOptions = PlatformFilterOption.ForRegistry(registry);
        var generalFirst = new PlatformFilterOption(null, "Общие");
        var customOptions = PlatformFilterOption.ForRegistry(registry, generalFirst);

        Assert.Multiple(() =>
        {
            Assert.That(defaultOptions, Is.EqualTo(new PlatformFilterOption[]
            {
                PlatformFilterOption.All,
                new(StreamingPlatform.Twitch, "Twitch"),
                new(StreamingPlatform.VkLive, "VK Видео Live"),
            }));
            Assert.That(customOptions, Is.EqualTo(new PlatformFilterOption[]
            {
                generalFirst,
                new(StreamingPlatform.Twitch, "Twitch"),
                new(StreamingPlatform.VkLive, "VK Видео Live"),
            }));
        });
    }

    [Test]
    public void ChatDiagnosticsCard_renders_third_platform_from_PlatformChats()
    {
        var snapshot = DiagnosticsSnapshots.Empty() with
        {
            PlatformChats =
            [
                new(StreamingPlatform.Twitch, "Twitch", new(ConnectionState.Connected, "tw_chan", DiagnosticsSnapshots.CapturedAt.AddMinutes(-10), DiagnosticsSnapshots.CapturedAt.AddMinutes(-1))),
                new(StreamingPlatform.VkLive, "VK Видео Live", new(ConnectionState.Failed, "vk_chan", null, null, ChatReadIssue.MissingBotAuth)),
            ],
        };

        var publisher = new DiagnosticsSnapshotPublisher(
            new ManualUiDispatcher(),
            () => snapshot,
            NullLogger<DiagnosticsSnapshotPublisher>.Instance);
        var card = new ChatDiagnosticsCardViewModel(publisher);
        card.SetActive(true);

        Assert.Multiple(() =>
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Error));
            Assert.That(card.Rows.Single(r => r.Key == "Twitch").Value, Is.EqualTo("подключено"));
            Assert.That(card.Rows.Single(r => r.Key == "VK Видео Live").Value, Is.EqualTo("чат не читается: токен бота недоступен"));
            Assert.That(card.Rows.Single(r => r.Key == "Канал VK Видео Live").Value, Is.EqualTo("vk_chan"));
        });
    }

    [Test]
    public async Task StreamMonitoring_respects_explicit_UsesSingleMonitoringRole_contract()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var singleRoleVk = new StubChatPlatform(
            StreamingPlatform.VkLive,
            "VK Видео Live",
            PlatformCapabilities.None,
            true,
            StreamStatus.Offline,
            null,
            "vk-chan",
            usesSingleMonitoringRole: true);
        using var singleVm = new StreamMonitoringViewModel(bus, new StubPlatformRegistry(singleRoleVk));

        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.VkLive, PlatformAuthRole.Bot, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.VkLive, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.VkLive, PlatformAuthRole.Bot, StreamMonitoringStatus.Disconnected));

        Assert.Multiple(() =>
        {
            Assert.That(singleVm.Groups.Single().Bot.IsReported, Is.False);
            Assert.That(singleVm.Groups.Single().Broadcaster.IsReported, Is.True);
            Assert.That(singleVm.Groups.Single().Broadcaster.Text, Is.EqualTo("Стример: работает"));
        });

        var dualRoleVk = new StubChatPlatform(
            StreamingPlatform.VkLive,
            "VK Видео Live",
            PlatformCapabilities.None,
            true,
            StreamStatus.Offline,
            null,
            "vk-chan",
            usesSingleMonitoringRole: false);
        using var dualVm = new StreamMonitoringViewModel(bus, new StubPlatformRegistry(dualRoleVk));

        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.VkLive, PlatformAuthRole.Bot, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.VkLive, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Connected));

        Assert.Multiple(() =>
        {
            Assert.That(dualVm.Groups.Single().Bot.IsReported, Is.True);
            Assert.That(dualVm.Groups.Single().Broadcaster.IsReported, Is.True);
        });
    }

    [Test]
    public void CommandsPage_whisper_notice_follows_selected_platform_permission_and_name()
    {
        var hasVkWhisperPermission = true;
        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.Whisper | PlatformCapabilities.ChannelMetadataEdit, true, StreamStatus.Offline, null, "tw"),
            new StubChatPlatform(
                StreamingPlatform.VkLive,
                "VK Видео Live",
                PlatformCapabilities.Whisper,
                () => true,
                StreamStatus.Offline,
                null,
                "vk",
                hasWhisperPermission: () => hasVkWhisperPermission));

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        var cmdStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        cmdStore.Mutate(s => s.DefaultResponseTarget = CommandResponseTarget.Whisper | CommandResponseTarget.Chat);
        var usage = new CommandUsageRepository(TimeProvider.System);
        var processor = new ChatCommandProcessor(
            [new StubCommand("title", PlatformCapabilities.ChannelMetadataEdit), new StubCommand("помощь", PlatformCapabilities.None)],
            cmdStore,
            usage,
            NullLogger<ChatCommandProcessor>.Instance,
            "!");

        var page = new CommandsPageViewModel(
            processor,
            cmdStore,
            settingsManager,
            usage,
            new FakeUnsavedChangesPrompt(),
            new AccountsStore(null, Path.Combine(_root, "accounts.json")),
            NullLogger<CommandsPageViewModel>.Instance,
            registry);

        page.CommandScope = page.CommandScopes.Single(scope => scope.Platform == StreamingPlatform.VkLive);
        Assert.That(page.WhisperNotice, Is.Null);

        hasVkWhisperPermission = false;
        page.RefreshCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(page.WhisperNotice, Does.Contain("VK Видео Live"));
            Assert.That(page.WhisperNotice, Does.Not.Contain("Twitch"));
        });
    }

    [Test]
    public void CommandsPage_ThirdPlatformShowsPublicReplyAndMarksUnsupportedCommands()
    {
        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.Whisper | PlatformCapabilities.ChannelMetadataEdit, true, StreamStatus.Offline, null, "tw"),
            new StubChatPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, true, StreamStatus.Offline, null, "vk"));

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        var cmdStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        cmdStore.Mutate(s => s.DefaultResponseTarget = CommandResponseTarget.Whisper | CommandResponseTarget.Chat);
        var usage = new CommandUsageRepository(TimeProvider.System);
        var processor = new ChatCommandProcessor(
            [new StubCommand("title", PlatformCapabilities.ChannelMetadataEdit), new StubCommand("помощь", PlatformCapabilities.None)],
            cmdStore,
            usage,
            NullLogger<ChatCommandProcessor>.Instance,
            "!");

        var page = new CommandsPageViewModel(
            processor,
            cmdStore,
            settingsManager,
            usage,
            new FakeUnsavedChangesPrompt(),
            new AccountsStore(null, Path.Combine(_root, "accounts.json")),
            NullLogger<CommandsPageViewModel>.Instance,
            registry);

        page.CommandScope = page.CommandScopes.Single(scope => scope.Platform == StreamingPlatform.VkLive);
        var titleRow = page.Rows.Single(r => r.Canonical == "title");

        Assert.Multiple(() =>
        {
            Assert.That(page.WhisperNotice, Does.Contain("VK Видео Live").And.Contain("в общем чате"));
            Assert.That(titleRow.IsClosedByCode, Is.True);
            Assert.That(titleRow.CodeAccessText, Is.EqualTo("Недоступна (VK Видео Live)"));
            Assert.That(titleRow.AccessText, Is.EqualTo("Недоступна (VK Видео Live)"));
        });
    }

    [Test]
    public async Task MultistreamTile_and_MultistreamPage_work_with_third_platform_enablement_source()
    {
        var enabled = false;
        var enablementSource = new StubEnablementSource(StreamingPlatform.VkLive, () => enabled);
        var vk = new StubChatPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, () => enabled, StreamStatus.Offline, null, "vk-chan");
        var registry = new StubPlatformRegistry(vk);
        var gate = new SettingsWriteGate();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"), gate: gate);
        var manager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"), gate);
        var server = new KestrelHttpServer(null!, manager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var dispatcher = new ManualUiDispatcher();
        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, platformRegistry: registry);
        var groups = new ChannelGroupsSettingsSectionViewModel(catalog, new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance));

        using var settings = new SettingsPageViewModel(
            [new ChannelsSettingsSectionContributor(editor, groups, catalog)],
            manager, gate, server, bus, NullLogger<SettingsPageViewModel>.Instance,
            DispatchProxy.Create<IDialogService, InertProxy>(), new MemorySettings(), platformRegistry: registry);
        await using var connection = new BotConnectionManager(new AppHost([], NullLogger<AppHost>.Instance), [], null, bus, NullLogger<BotConnectionManager>.Instance);

        using var tile = new MultistreamTileViewModel(
            registry, catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher,
            enablementSources: [enablementSource]);
        tile.SetPageOnScreen(true);

        var page = new MultistreamPageViewModel(settings, tile, []);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Rows.Single().IsPlatformEnabled, Is.False);
            Assert.That(page.PlatformReadinessText, Is.EqualTo("Площадка: отключена"));
        });

        enabled = true;
        enablementSource.NotifyChanged();
        Assert.Multiple(() =>
        {
            Assert.That(tile.Rows.Single().IsPlatformEnabled, Is.True);
            Assert.That(page.PlatformReadinessText, Is.EqualTo("Площадка: включена"));
        });

        var adapterEnabled = false;
        var unavailableVk = new StubChatPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, () => adapterEnabled, StreamStatus.Offline, null, "vk-chan");
        using var unavailableTile = new MultistreamTileViewModel(
            new StubPlatformRegistry(unavailableVk), catalog, connection, new PlatformAuthRegistry([]), [], bus, dispatcher,
            enablementSources: [enablementSource]);
        unavailableTile.SetPageOnScreen(true);

        Assert.Multiple(() =>
        {
            Assert.That(unavailableTile.Rows.Single().IsPlatformEnabled, Is.False);
            Assert.That(unavailableTile.Rows.Single().IsBlockedByDebugSession, Is.False);
            Assert.That(unavailableTile.Rows.Single().StatusBadgeText, Is.EqualTo("Отключён"));
        });
    }

    [Test]
    public async Task StreamInfo_StreamPreview_and_History_include_third_platform_via_registry_and_enablement_source()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var dispatcher = new ManualUiDispatcher();
        var vkEnabled = true;
        var enablement = new StubEnablementSource(StreamingPlatform.VkLive, () => vkEnabled);
        var vkInfo = new StreamInfo { Title = "VK Эфир", ViewerCount = 30, StartedAt = DateTime.UtcNow.AddMinutes(-3), ThumbnailUrl = "" };
        var vk = new StubChatPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, () => vkEnabled, StreamStatus.Online, vkInfo, "vk-chan", "https://live.vkvideo.ru/vk-chan");
        var registry = new StubPlatformRegistry(vk);

        using var infoTile = new StreamInfoTileViewModel(
            registry, bus, NullLogger<StreamInfoTileViewModel>.Instance, dispatcher, new RecordingLauncher(),
            enablementSources: [enablement]);
        using var previewTile = new StreamPreviewTileViewModel(
            registry, bus, NullLogger<StreamPreviewTileViewModel>.Instance, dispatcher,
            enablementSources: [enablement]);

        await bus.PublishAsync(new PlatformMultipleBroadcastsChanged(StreamingPlatform.VkLive, "vk-chan", 2));
        Assert.That(infoTile.HasMultipleBroadcastsNotice, Is.True);
        await bus.PublishAsync(new PlatformMultipleBroadcastsChanged(StreamingPlatform.VkLive, "vk-chan", 1));
        Assert.That(infoTile.HasMultipleBroadcastsNotice, Is.False);

        var historyStore = new BroadcastHistoryStore(null, Path.Combine(_root, "broadcasts.json"), Path.Combine(_root, "sessions.json"));
        var now = DateTimeOffset.UtcNow;
        historyStore.TryAppend(new BroadcastRecord
        {
            StartedAt = now.AddHours(-1),
            EndedAt = now,
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = StreamingPlatform.VkLive,
                    Channel = "vk-chan",
                    Title = "VK Эфир",
                    StartedAt = now.AddHours(-1),
                    EndedAt = now,
                    MessageCount = 5,
                    ChatterCount = 2,
                },
            ],
        });
        using var historyPage = new StreamHistoryPageViewModel(
            historyStore,
            new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance),
            new MemorySettings(),
            new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance),
            bus,
            platformRegistry: registry);
        historyPage.SelectedRow = historyPage.Sessions[0];

        Assert.Multiple(() =>
        {
            Assert.That(infoTile.SelectedPlatform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(infoTile.SelectedPlatformName, Is.EqualTo("VK Видео Live"));
            Assert.That(previewTile.SelectedPlatform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(previewTile.SelectedPlatformName, Is.EqualTo("VK Видео Live"));
            Assert.That(historyPage.PlatformOptions.Select(static o => o.Platform), Is.EqualTo(new StreamingPlatform?[] { null, StreamingPlatform.VkLive }));
            Assert.That(historyPage.DetailPlatformBreakdown, Has.Count.EqualTo(1));
            Assert.That(historyPage.DetailPlatformBreakdown[0].DisplayName, Is.EqualTo("VK Видео Live"));
        });
    }

    private sealed class StubEnablementSource(StreamingPlatform platform, Func<bool> isEnabled) : IPlatformEnablementSource
    {
        public StreamingPlatform Platform => platform;

        public bool IsEnabledInSettings => isEnabled();

        public event Action? Changed;

        public void NotifyChanged() => Changed?.Invoke();
    }

    private sealed class StubChatPlatform(
        StreamingPlatform id,
        string displayName,
        PlatformCapabilities capabilities,
        Func<bool> isEnabled,
        StreamStatus status,
        StreamInfo? info,
        string? configuredChannel,
        string? streamUrl = null,
        bool usesSingleMonitoringRole = false,
        Func<bool?>? hasWhisperPermission = null) : IChatPlatform
    {
        public StubChatPlatform(
            StreamingPlatform id,
            string displayName,
            PlatformCapabilities capabilities,
            bool isEnabled,
            StreamStatus status,
            StreamInfo? info,
            string? configuredChannel,
            string? streamUrl = null,
            bool usesSingleMonitoringRole = false,
            Func<bool?>? hasWhisperPermission = null)
            : this(id, displayName, capabilities, () => isEnabled, status, info, configuredChannel, streamUrl, usesSingleMonitoringRole, hasWhisperPermission)
        {
        }

        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => capabilities;
        public bool IsEnabled => isEnabled();
        public bool IsConnected => false;
        public bool UsesSingleMonitoringRole => usesSingleMonitoringRole;
        public bool? HasWhisperPermission => hasWhisperPermission?.Invoke();
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus(status, info);
        public IPlatformMessenger Messenger => null!;
        public string? ConfiguredChannel => configuredChannel;
        public string? BuildStreamUrl(StreamInfo? stream = null) => streamUrl;
    }

    private sealed class StubStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;
        public StreamInfo? CurrentStream => info;
        public StreamStatusSnapshot GetSnapshot() => new(status, info, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;
        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = Find(platform);
            return chatPlatform is not null;
        }
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubCommand(string canonical, PlatformCapabilities required) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public PlatformCapabilities RequiredCapabilities => required;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult<OutgoingMessage?>(null);
    }

    private sealed class RecordingLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class FakeUnsavedChangesPrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesDecision Ask(string subject, string action) => UnsavedChangesDecision.Discard;
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is not null && targetMethod.ReturnType != typeof(void) && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
