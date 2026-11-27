using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Twitch.EventSub;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class ChannelSwitchCoordinatorTests
{
    private string _root = null!;
    private ManualTimeProvider _time = null!;
    private InMemoryEventBus _eventBus = null!;
    private ChannelCatalogStore _catalog = null!;
    private PlatformChannelRegistry _channelRegistry = null!;
    private FakeSwitchAdapter _adapter = null!;
    private FakeSwitchMonitor _monitor = null!;
    private IBroadcastChannelLifecycle _broadcastLifecycle = null!;
    private BotConnectionManager _connectionManager = null!;
    private FakePlatformConnection _connectionAdapter = null!;
    private ChannelSwitchCoordinator _coordinator = null!;

    private static readonly ChannelKey ChanA = new(StreamingPlatform.YouTube, "UC_A");
    private static readonly ChannelKey ChanB = new(StreamingPlatform.YouTube, "UC_B");
    private static readonly ChannelKey ChanC = new(StreamingPlatform.YouTube, "UC_C");

    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _time = new();
        _eventBus = new(NullLogger<InMemoryEventBus>.Instance);
        _catalog = new(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_root, "channels.json"));
        SeedThreeChannels(ChannelSwitchMode.Manual);

        _channelRegistry = new();
        _channelRegistry.SetBinding(new(ChanA, "Канал A", AuthContextIds.Default, AuthContextIds.Default, 0));

        _adapter = new(StreamingPlatform.YouTube);
        _monitor = new(StreamingPlatform.YouTube);
        _broadcastLifecycle = Substitute.For<IBroadcastChannelLifecycle>();

        var appHost = new AppHost([], NullLogger<AppHost>.Instance);
        _connectionAdapter = new(StreamingPlatform.YouTube, _channelRegistry);
        _connectionManager = new(
            appHost,
            [_connectionAdapter],
            chatPipelineHandler: null,
            _eventBus,
            NullLogger<BotConnectionManager>.Instance);
        _connectionManager.StartConnection();
        await _connectionManager.WaitForConnectionAsync();

        var settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, null);
        settingsManager.Current.Returns(new AppSettings());
        var twitchAccounts = new AccountsStore(NullLogger<AccountsStore>.Instance, Path.Combine(_root, "tw.json"));
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_root, "yt-acc.json"));
        var ytSettings = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_root, "yt.json"));
        var targetChannel = new FakeTargetChannelProvider("OriginA");
        var reconciler = new ChannelCatalogReconciler(
            _catalog,
            settingsManager,
            twitchAccounts,
            ytSettings,
            ytAccounts,
            targetChannel,
            NullLogger<ChannelCatalogReconciler>.Instance);

        var authService = Substitute.For<IPlatformAuthService>();
        authService.Platform.Returns(StreamingPlatform.YouTube);
        authService.GetAccessTokenAsync(Arg.Any<PlatformAuthRole>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("bot-token"));
        authService.GetAccessTokenAsync(Arg.Any<PlatformAuthRole>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>("bot-token"));
        var authRegistry = new PlatformAuthRegistry([authService]);

        _coordinator = new(
            _catalog,
            _channelRegistry,
            reconciler,
            authRegistry,
            _broadcastLifecycle,
            [_adapter],
            [_monitor],
            targetChannel,
            _eventBus,
            _time,
            NullLogger<ChannelSwitchCoordinator>.Instance,
            _connectionManager);
    }

    [TearDown]
    public async Task TearDown()
    {
        _coordinator.Dispose();
        await _connectionManager.DisposeAsync();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task Manual_mode_ignores_other_channel_starts_and_allows_explicit_manual_switch()
    {
        AutoDiscoverPrompt? receivedPrompt = null;
        using var sub = _eventBus.Subscribe<AutoDiscoverPromptChanged>(e => receivedPrompt = e.Prompt);

        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "sb-1");
        Assert.Multiple(() =>
        {
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId, Is.EqualTo("UC_A"));
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanA));
            Assert.That(receivedPrompt, Is.Null);
        });

        _adapter.Probes[ChanB] = new(false, false, null);
        _monitor.SetSnapshot(MakeSnapshot(ChanB, StreamStatus.Offline, null));
        var manual = await _coordinator.SwitchActiveChannelAsync(StreamingPlatform.YouTube, ChanB);

        Assert.Multiple(() =>
        {
            Assert.That(manual.Succeeded, Is.True);
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId, Is.EqualTo("UC_B"));
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanB));
            Assert.That(_connectionAdapter.RetargetedTo[^1], Is.EqualTo(ChanB));
        });
        await _broadcastLifecycle.Received(1).LeaveActiveChannelAsync(StreamingPlatform.YouTube, ChanA, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Disconnected_catalog_switch_stages_a_new_channel_and_publishes_the_previous_saved_active()
    {
        await _connectionManager.StopAsync(StreamingPlatform.YouTube);
        _channelRegistry.Clear(StreamingPlatform.YouTube);
        var newKey = new ChannelKey(StreamingPlatform.YouTube, "UC_DRAFT");
        ActiveChannelChanged? published = null;
        using var subscription = _eventBus.Subscribe<ActiveChannelChanged>(message => published = message);
        var draft = new ChannelCatalogEntry { Platform = newKey.Platform, ChannelId = newKey.ChannelId, IsConfigured = true };

        var result = await _coordinator.SwitchCatalogChannelAsync(draft);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(published?.PreviousChannel, Is.EqualTo(ChanA));
            Assert.That(published?.NewChannel, Is.EqualTo(newKey));
            Assert.That(_catalog.GetPlatformSettings(newKey.Platform).ActiveChannelId, Is.EqualTo(newKey.ChannelId));
            Assert.That(_catalog.FindEntry(newKey)?.IsConfigured, Is.True);
            Assert.That(_connectionAdapter.RetargetedTo, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Refused_catalog_switch_keeps_old_binding_and_never_persists_the_draft_target(bool passesPreflight)
    {
        var target = new ChannelKey(StreamingPlatform.YouTube, "UC_DRAFT");
        _connectionAdapter.FailTargets.Add(target);
        var draft = new ChannelCatalogEntry
        {
            Platform = target.Platform,
            ChannelId = target.ChannelId,
            IsConfigured = true,
            BotAccountId = "bot-1",
            BotVerifiedSubjectId = passesPreflight ? "bot-1" : null,
        };

        var result = await _coordinator.SwitchCatalogChannelAsync(draft);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ChannelSwitchOutcome.FailedKeptPrevious));
            Assert.That(_catalog.GetPlatformSettings(target.Platform).ActiveChannelId, Is.EqualTo(ChanA.ChannelId));
            Assert.That(_catalog.FindEntry(target), Is.Null);
            Assert.That(_channelRegistry.GetBinding(target.Platform)?.Key, Is.EqualTo(ChanA));
            Assert.That(_connectionAdapter.RetargetedTo, passesPreflight ? Is.EqualTo(new[] { ChanA }) : Is.Empty);
        });
    }

    [Test]
    public async Task Manual_switch_from_settings_preserves_the_saved_next_channel()
    {
        const string NextId = "UC_C";
        _catalog.Mutate(settings =>
        {
            settings.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = NextId, IsConfigured = true });
            settings.Platforms.Single(platform => platform.Platform == StreamingPlatform.YouTube).PreselectedNextChannelId = NextId;
        });
        _adapter.Probes[ChanB] = new(false, false, null);

        var result = await _coordinator.SwitchActiveChannelAsync(StreamingPlatform.YouTube, ChanB, preservePreselectedNext: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            var routing = _catalog.GetPlatformSettings(StreamingPlatform.YouTube);
            Assert.That(routing.ActiveChannelId, Is.EqualTo(ChanB.ChannelId));
            Assert.That(routing.PreselectedNextChannelId, Is.EqualTo(NextId));
        });
    }

    [Test]
    public async Task PreselectedNext_switches_on_online_transition_or_immediate_arm_and_clears_target()
    {
        await _coordinator.SetSwitchModeAsync(StreamingPlatform.YouTube, ChannelSwitchMode.PreselectedNext);
        _adapter.Probes[ChanB] = new(false, true, null);
        var armed = await _coordinator.SetPreselectedNextAsync(StreamingPlatform.YouTube, ChanB);
        Assert.That(armed, Is.Null);
        Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).PreselectedNextChannelId, Is.EqualTo("UC_B"));

        var streamB = new StreamInfo { Id = "yt-b-1", StartedAt = _time.GetUtcNow().UtcDateTime, Title = "Go B" };
        _adapter.Probes[ChanB] = new(true, true, streamB, "chat-b");
        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "yt-b-1");

        Assert.Multiple(() =>
        {
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId, Is.EqualTo("UC_B"));
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).PreselectedNextChannelId, Is.Null);
            Assert.That(_connectionAdapter.RetargetedTo, Contains.Item(ChanB));
        });

        var streamC = new StreamInfo { Id = "yt-c-1", StartedAt = _time.GetUtcNow().UtcDateTime, Title = "Already live C" };
        _adapter.Probes[ChanC] = new(true, true, streamC, "chat-c");
        _monitor.SetSnapshot(MakeSnapshot(ChanC, StreamStatus.Online, "yt-c-1"));

        var immediate = await _coordinator.SetPreselectedNextAsync(StreamingPlatform.YouTube, ChanC);
        Assert.Multiple(() =>
        {
            Assert.That(immediate?.Succeeded, Is.True);
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).ActiveChannelId, Is.EqualTo("UC_C"));
            Assert.That(_catalog.GetPlatformSettings(StreamingPlatform.YouTube).PreselectedNextChannelId, Is.Null);
        });
    }

    [Test]
    public async Task AutoDiscover_suppresses_baseline_deduplicates_polls_handles_two_starts_and_ignores_late_left_A()
    {
        _monitor.SetSnapshot(MakeSnapshot(ChanA, StreamStatus.Online, "s-a-base"));
        _monitor.SetSnapshot(MakeSnapshot(ChanB, StreamStatus.Online, "s-b-base"));
        _monitor.SetSnapshot(MakeSnapshot(ChanC, StreamStatus.Offline, null));

        var prompts = new List<AutoDiscoverPrompt?>();
        using var sub = _eventBus.Subscribe<AutoDiscoverPromptChanged>(e => prompts.Add(e.Prompt));

        await _coordinator.SetSwitchModeAsync(StreamingPlatform.YouTube, ChannelSwitchMode.AutoDiscover);
        Assert.That(_coordinator.GetActivePrompt(StreamingPlatform.YouTube), Is.Null);

        await PublishObservedAsync(ChanB, StreamStatus.Online, StreamStatus.Online, "s-b-base");
        Assert.That(_coordinator.GetActivePrompt(StreamingPlatform.YouTube), Is.Null);

        await PublishObservedAsync(ChanB, StreamStatus.Online, StreamStatus.Offline, "s-b-base");
        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "s-b-new");
        await PublishObservedAsync(ChanB, StreamStatus.Online, StreamStatus.Online, "s-b-new");
        await PublishObservedAsync(ChanC, StreamStatus.Offline, StreamStatus.Online, "s-c-new");

        var currentPrompt = _coordinator.GetActivePrompt(StreamingPlatform.YouTube);
        Assert.That(currentPrompt, Is.Not.Null);
        Assert.That(currentPrompt!.Candidates.Select(c => c.Channel), Is.EqualTo(new[] { ChanB, ChanC }));

        _adapter.Probes[ChanB] = new(true, true, new() { Id = "s-b-new", StartedAt = _time.GetUtcNow().UtcDateTime }, "chat-b");
        var confirmed = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanB);
        Assert.Multiple(() =>
        {
            Assert.That(confirmed.Succeeded, Is.True);
            Assert.That(_coordinator.GetActivePrompt(StreamingPlatform.YouTube), Is.Null);
        });

        await PublishObservedAsync(ChanA, StreamStatus.Online, StreamStatus.Online, "s-a-base");
        await PublishObservedAsync(ChanA, StreamStatus.Online, StreamStatus.Offline, "s-a-base");
        Assert.That(_coordinator.GetActivePrompt(StreamingPlatform.YouTube), Is.Null);
    }

    [Test]
    public async Task AutoDiscover_reverification_and_transport_failure_preserve_A_when_alive_and_report_when_broken()
    {
        _monitor.SetSnapshot(MakeSnapshot(ChanA, StreamStatus.Online, "sa"));
        _monitor.SetSnapshot(MakeSnapshot(ChanB, StreamStatus.Offline, null));
        _monitor.SetSnapshot(MakeSnapshot(ChanC, StreamStatus.Offline, null));
        await _coordinator.SetSwitchModeAsync(StreamingPlatform.YouTube, ChannelSwitchMode.AutoDiscover);
        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "s-b-race");

        var failures = new List<ChannelSwitchFailed>();
        using var sub = _eventBus.Subscribe<ChannelSwitchFailed>(e => failures.Add(e));

        _adapter.Probes[ChanB] = new(false, false, null, null, "Поток завершился");
        var endedBeforeConfirm = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanB);
        Assert.Multiple(() =>
        {
            Assert.That(endedBeforeConfirm.Outcome, Is.EqualTo(ChannelSwitchOutcome.TargetNoLongerLive));
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanA));
            Assert.That(failures, Is.Empty);
        });

        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "s-b-unreachable");
        _adapter.Probes[ChanB] = new(true, false, new() { Id = "s-b-unreachable", StartedAt = _time.GetUtcNow().UtcDateTime }, null, "Чат закрыт");
        var unreachable = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanB);
        Assert.Multiple(() =>
        {
            Assert.That(unreachable.Outcome, Is.EqualTo(ChannelSwitchOutcome.TargetChatUnreachable));
            Assert.That(failures[^1].KeptPreviousChat, Is.True);
        });

        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "s-b-2");
        _adapter.Probes[ChanB] = new(true, true, new() { Id = "s-b-2", StartedAt = _time.GetUtcNow().UtcDateTime });
        _connectionAdapter.FailTargets.Add(ChanB);

        var keptA = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanB);
        Assert.Multiple(() =>
        {
            Assert.That(keptA.Outcome, Is.EqualTo(ChannelSwitchOutcome.FailedKeptPrevious));
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanA));
            Assert.That(failures[^1].KeptPreviousChat, Is.True);
        });

        _connectionAdapter.FailTargets.Add(ChanA);
        await PublishObservedAsync(ChanC, StreamStatus.Offline, StreamStatus.Online, "s-c-2");
        _adapter.Probes[ChanC] = new(true, true, new() { Id = "s-c-2", StartedAt = _time.GetUtcNow().UtcDateTime });
        _connectionAdapter.FailTargets.Add(ChanC);

        var lostBoth = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanC);
        Assert.Multiple(() =>
        {
            Assert.That(lostBoth.Outcome, Is.EqualTo(ChannelSwitchOutcome.FailedNoActiveChat));
            Assert.That(_connectionManager.IsPlatformConnected(StreamingPlatform.YouTube), Is.False);
            Assert.That(failures[^1].KeptPreviousChat, Is.False);
        });
    }

    [Test]
    public async Task AutoDiscover_rejects_mismatched_stream_id()
    {
        await _coordinator.SetSwitchModeAsync(StreamingPlatform.YouTube, ChannelSwitchMode.AutoDiscover);
        await PublishObservedAsync(ChanB, StreamStatus.Offline, StreamStatus.Online, "sb-first");

        _adapter.Probes[ChanB] = new(true, true, new() { Id = "sb-second", StartedAt = _time.GetUtcNow().UtcDateTime });
        var mismatched = await _coordinator.ConfirmAutoDiscoverCandidateAsync(StreamingPlatform.YouTube, ChanB);
        Assert.Multiple(() =>
        {
            Assert.That(mismatched.Outcome, Is.EqualTo(ChannelSwitchOutcome.TargetNoLongerLive));
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanA));
        });
    }

    [Test]
    public async Task Manual_switch_rejects_channel_without_bot_assignment_before_retargeting()
    {
        _catalog.Mutate(s =>
        {
            var target = s.Channels.Single(c => c.ChannelId == "UC_B");
            target.BotAccountId = null;
            target.BotVerifiedSubjectId = null;
        });

        _adapter.Probes[ChanB] = new(true, true, new() { Id = "s-b-unassigned", StartedAt = _time.GetUtcNow().UtcDateTime });
        var result = await _coordinator.SwitchActiveChannelAsync(StreamingPlatform.YouTube, ChanB);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(ChannelSwitchOutcome.FailedKeptPrevious));
            Assert.That(result.ErrorMessage, Does.Contain("Подтвердите доступ"));
            Assert.That(_connectionAdapter.RetargetedTo, Is.Empty);
            Assert.That(_channelRegistry.GetBinding(StreamingPlatform.YouTube)?.Key, Is.EqualTo(ChanA));
        });
    }

    private void SeedThreeChannels(ChannelSwitchMode mode)
    {
        _catalog.Mutate(s =>
        {
            s.Channels =
            [
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "Канал A", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "bot-1", BotVerifiedSubjectId = "bot-1" },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "Канал B", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "bot-1", BotVerifiedSubjectId = "bot-1" },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_C", DisplayName = "Канал C", IsConfigured = true, MonitoringEnabled = true, BotAccountId = "bot-1", BotVerifiedSubjectId = "bot-1" },
            ];
            s.Platforms = [new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_A", SwitchMode = mode }];
        });
    }

    private Task PublishObservedAsync(ChannelKey channel, StreamStatus prev, StreamStatus curr, string? streamId)
    {
        var previous = MakeSnapshot(channel, prev, prev == StreamStatus.Online ? streamId : null);
        var current = MakeSnapshot(channel, curr, curr == StreamStatus.Online ? streamId : null);
        _monitor.SetSnapshot(current);
        return _eventBus.PublishAsync(new ChannelStreamObserved(previous, current));
    }

    private ChannelMonitoringSnapshot MakeSnapshot(ChannelKey channel, StreamStatus status, string? streamId)
    {
        StreamInfo? info = status == StreamStatus.Online
            ? new() { Id = streamId ?? "stream", StartedAt = _time.GetUtcNow().UtcDateTime, Title = $"Live {channel.ChannelId}" }
            : null;
        return new(channel, $"Канал {channel.ChannelId}", ChannelMonitoringAvailability.Active, MonitoringUnavailableReason.None, null, status, info, _time.GetUtcNow(), false);
    }

    private sealed class FakeSwitchAdapter(StreamingPlatform platform) : IPlatformChannelAdapter
    {
        public Dictionary<ChannelKey, ChannelLiveProbeResult> Probes { get; } = [];
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.BackgroundStreamMonitoring | PlatformCapabilities.MultiChannelMonitoring;

        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedChannelIdentity?>(null);

        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelAccessVerification(true, "sub"));

        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken)
            => Task.FromResult(Probes.TryGetValue(channel.Key, out var probe) ? probe : new ChannelLiveProbeResult(false, true, null));
    }

    private sealed class FakeSwitchMonitor(StreamingPlatform platform) : IPlatformChannelMonitor
    {
        private readonly Dictionary<ChannelKey, ChannelMonitoringSnapshot> _snapshots = [];
        public StreamingPlatform Platform => platform;
        public string Name => "FakeMonitor";
        public int StartOrder => 10;
        public void SetSnapshot(ChannelMonitoringSnapshot snap) => _snapshots[snap.Channel] = snap;
        public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel) =>
            _snapshots.TryGetValue(channel, out var snap)
                ? snap
                : new(channel, channel.ChannelId, ChannelMonitoringAvailability.Active, MonitoringUnavailableReason.None, null, StreamStatus.Unknown, null, null, false);
        public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots() => [.. _snapshots.Values];
        public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePlatformConnection(StreamingPlatform platform, PlatformChannelRegistry registry) : IPlatformConnectionAdapter
    {
        public HashSet<ChannelKey> FailTargets { get; } = [];
        public List<ChannelKey> RetargetedTo { get; } = [];
        public StreamingPlatform Platform => platform;
        public bool IsEnabled => true;
        public PlatformHost Host { get; } = new(platform, [], NullLogger<PlatformHost>.Instance);

        public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            await Host.StartAsync(progress, cancellationToken);
            return "Канал A";
        }

        public void EndSession() { }

        public Task<string> RetargetSessionAsync(PlatformChatSessionTarget target, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (FailTargets.Contains(target.Channel.Key))
            {
                throw new InvalidOperationException("Chat connect failed");
            }

            RetargetedTo.Add(target.Channel.Key);
            registry.SetBinding(new(target.Channel.Key, target.Channel.DisplayName, target.Channel.BotContextId ?? AuthContextIds.Default, target.Channel.BroadcasterContextId ?? AuthContextIds.Default, target.SwitchGeneration));
            return Task.FromResult(target.Channel.DisplayName);
        }
    }
}
