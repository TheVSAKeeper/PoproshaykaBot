using System.Diagnostics.CodeAnalysis;
using System.IO;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Channels;

[TestFixture]
public sealed class Stage3MultiChannelWpfTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-wpf-mc-{Guid.NewGuid():N}");
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
    public async Task ChannelsContributor_saves_switch_mode_preselected_next_preserves_live_active_and_respects_SettingsWriteGate()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_C", DisplayName = "Канал C", IsConfigured = true, MonitoringEnabled = true },
            ];
            s.Platforms =
            [
                new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_A", SwitchMode = ChannelSwitchMode.Manual },
            ];
        });

        var stats = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog);
        var groupsVm = new ChannelGroupsSettingsSectionViewModel(catalog, stats);
        var contributor = new ChannelsSettingsSectionContributor(catalogVm, groupsVm, catalog);

        contributor.Load(new SettingsSessionState { Settings = new() });
        using var dirtySub = contributor.SubscribeDirty(new SettingsSessionState { Settings = new() }, static () => { }, static () => { });
        catalogVm.SelectedPlatform = StreamingPlatform.YouTube;
        catalogVm.SelectedSwitchModeOption = catalogVm.SwitchModeOptions.First(o => o.Mode == ChannelSwitchMode.PreselectedNext);
        catalogVm.SelectedPreselectedNextOption = catalogVm.PreselectedNextOptions.First(o => o.ChannelId == "UC_B");

        catalog.Mutate(s =>
        {
            s.Platforms[0].ActiveChannelId = "UC_C";
        });

        var saved = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Written, Is.True);
            var routing = catalog.GetPlatformSettings(StreamingPlatform.YouTube);
            Assert.That(routing.SwitchMode, Is.EqualTo(ChannelSwitchMode.PreselectedNext));
            Assert.That(routing.PreselectedNextChannelId, Is.EqualTo("UC_B"));
            Assert.That(routing.ActiveChannelId, Is.EqualTo("UC_C"));
        });

        await gate.RunExternalWriteAsync(() => Task.FromResult(0), static _ => ["channels.json"]);
        catalogVm.SelectedSwitchModeOption = catalogVm.SwitchModeOptions.First(o => o.Mode == ChannelSwitchMode.Manual);
        var blocked = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.That(blocked.Written, Is.False);
    }

    [Test]
    public async Task ChannelCatalogVm_rejects_unresolved_channel_input_and_preserves_externally_cleared_preselected_next()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels2.json"), gate);
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-a", DisplayName = "A", IsConfigured = true },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw-b", DisplayName = "B", IsConfigured = true },
            ];
            s.Platforms =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ActiveChannelId = "tw-a",
                    SwitchMode = ChannelSwitchMode.PreselectedNext,
                    PreselectedNextChannelId = "tw-b",
                },
            ];
        });

        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter]);
        catalogVm.Load();
        catalogVm.NewChannelInput = "nonexistent_user";
        await catalogVm.AddChannelCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(catalogVm.VisibleChannels, Has.Count.EqualTo(2));
            Assert.That(catalogVm.StatusNotice, Is.Not.Empty);
        });

        var target = catalog.Load();
        target.Platforms[0].PreselectedNextChannelId = null;
        catalogVm.ApplyTo(target);
        Assert.That(target.Platforms[0].PreselectedNextChannelId, Is.Null);
    }

    private sealed class NullResolveAdapter(StreamingPlatform platform) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.BackgroundStreamMonitoring;
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelAccessVerification(true, "sub"));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelLiveProbeResult(false, true, null));
    }

    [Test]
    public async Task ChannelSwitchPromptBanner_handles_one_and_two_starts_dismiss_and_suppress()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var coordinator = new StubSwitchCoordinator();
        using var banner = new ChannelSwitchPromptBannerViewModel(
            coordinator,
            bus,
            NullLogger<ChannelSwitchPromptBannerViewModel>.Instance);

        var statuses = new List<(string Message, StatusSeverity Severity)>();
        banner.StatusReported += (msg, sev) => statuses.Add((msg, sev));

        Assert.That(banner.IsVisible, Is.False);

        var ytB = new ChannelKey(StreamingPlatform.YouTube, "UC_B");
        var ytC = new ChannelKey(StreamingPlatform.YouTube, "UC_C");
        var single = new AutoDiscoverPrompt(
            StreamingPlatform.YouTube,
            new(StreamingPlatform.YouTube, "UC_A"),
            "Канал A",
            [new(ytB, "Канал B", "sb", "Эфир B", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, single));

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.HasMultipleCandidates, Is.False);
            Assert.That(banner.SelectedCandidate?.Channel, Is.EqualTo(ytB));
        });

        await banner.ConfirmCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.Confirmed, Is.EqualTo(new[] { (StreamingPlatform.YouTube, ytB) }));
            Assert.That(statuses[^1].Severity, Is.EqualTo(StatusSeverity.Info));
        });

        var two = new AutoDiscoverPrompt(
            StreamingPlatform.YouTube,
            ytB,
            "Канал B",
            [
                new(new(StreamingPlatform.YouTube, "UC_A"), "Канал A", "sa", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new(ytC, "Канал C", "sc", "Эфир C", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            ]);
        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, two));

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.HasMultipleCandidates, Is.True);
            Assert.That(banner.Candidates, Has.Count.EqualTo(2));
        });

        banner.SelectedCandidate = banner.Candidates[1];
        await banner.ConfirmCommand.ExecuteAsync(null);
        Assert.That(coordinator.Confirmed[^1], Is.EqualTo((StreamingPlatform.YouTube, ytC)));

        await banner.DismissCommand.ExecuteAsync(null);
        Assert.That(coordinator.DismissedPlatforms, Contains.Item(StreamingPlatform.YouTube));

        await bus.PublishAsync(new AutoDiscoverPromptChanged(StreamingPlatform.YouTube, single));
        Assert.That(banner.IsVisible, Is.True);
        banner.Suppress();
        Assert.That(banner.IsVisible, Is.False);
    }

    [Test]
    public async Task StreamInfoTile_and_StreamMonitoringViewModel_surface_active_channel_preselected_next_and_monitoring_issues()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        var ytA = new ChannelKey(StreamingPlatform.YouTube, "UC_A");
        var ytB = new ChannelKey(StreamingPlatform.YouTube, "UC_B");
        catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A", IsConfigured = true, MonitoringEnabled = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B", IsConfigured = true, MonitoringEnabled = true },
            ];
            s.Platforms =
            [
                new()
                {
                    Platform = StreamingPlatform.YouTube,
                    ActiveChannelId = "UC_A",
                    SwitchMode = ChannelSwitchMode.PreselectedNext,
                    PreselectedNextChannelId = "UC_B",
                },
            ];
        });

        var platform = new StubPlatform(StreamingPlatform.YouTube, "YouTube", "Канал A");
        var registry = new StubPlatformRegistry(platform);
        var coordinator = new StubSwitchCoordinator();

        using var tile = new StreamInfoTileViewModel(
            registry,
            bus,
            NullLogger<StreamInfoTileViewModel>.Instance,
            new ManualUiDispatcher(),
            new NoOpLauncher(),
            catalogStore: catalog,
            switchCoordinator: coordinator);

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMultipleChannels, Is.True);
            Assert.That(tile.ActiveChannelDisplayName, Is.EqualTo("Канал A"));
            Assert.That(tile.HasPreselectedNext, Is.True);
            Assert.That(tile.PreselectedNextText, Does.Contain("Канал B"));
            Assert.That(tile.HasMonitoringIssue, Is.False);
        });

        var unavailableSnap = new ChannelMonitoringSnapshot(
            ytB,
            "Канал B",
            ChannelMonitoringAvailability.Unavailable,
            MonitoringUnavailableReason.AccessUnverified,
            "Доступ канала B не подтверждён",
            StreamStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            false);

        using var monitoring = new StreamMonitoringViewModel(bus, registry);
        await bus.PublishAsync(new ChannelMonitoringStateChanged(StreamingPlatform.YouTube, ytB, unavailableSnap));

        var ytGroup = monitoring.Groups.First(g => g.Platform == StreamingPlatform.YouTube);
        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMonitoringIssue, Is.True);
            Assert.That(tile.MonitoringIssueText, Does.Contain("Канал B"));
            Assert.That(ytGroup.Channels.IsReported, Is.True);
            Assert.That(ytGroup.Channels.Severity, Is.EqualTo(StatusSeverity.Error));
            Assert.That(ytGroup.Channels.Tooltip, Does.Contain("Доступ канала B"));
        });
    }

    private sealed class NoOpLauncher : IShellLauncher
    {
        public bool Open(string target) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubSwitchCoordinator : IChannelSwitchCoordinator
    {
        public List<(StreamingPlatform Platform, ChannelKey Channel)> Confirmed { get; } = [];
        public List<StreamingPlatform> DismissedPlatforms { get; } = [];

        public AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform) => null;
        public IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts() => [];

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, targetChannel));

        public Task<ChannelSwitchResult?> SetPreselectedNextAsync(StreamingPlatform platform, ChannelKey? targetChannel, CancellationToken cancellationToken = default)
            => Task.FromResult<ChannelSwitchResult?>(null);

        public Task SetSwitchModeAsync(StreamingPlatform platform, ChannelSwitchMode mode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(StreamingPlatform platform, ChannelKey candidateChannel, CancellationToken cancellationToken = default)
        {
            Confirmed.Add((platform, candidateChannel));
            return Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, candidateChannel));
        }

        public Task DismissAutoDiscoverPromptAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
        {
            DismissedPlatforms.Add(platform);
            return Task.CompletedTask;
        }

        public Task EvaluateStartupPendingAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform)!;
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform(StreamingPlatform id, string displayName, string configuredChannel) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => true;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus();
        public PlatformBotIdentity? BotIdentity => null;
        public string? ConfiguredChannel => configuredChannel;
        public IPlatformMessenger Messenger => null!;
        public IChatDecorations Decorations => NullChatDecorations.Instance;
        public string? BuildStreamUrl(StreamInfo? currentStream = null) => null;
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Offline;
        public StreamInfo? CurrentStream => null;
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, CurrentStream, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
