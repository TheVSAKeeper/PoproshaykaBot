using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
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
using PoproshaykaBot.Core.Twitch.Auth;
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
    public async Task Signing_in_from_a_channel_role_saves_the_grant_and_verifies_that_assignment()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        settings.Mutate(s =>
        {
            s.Twitch.ClientId = "test-client";
            s.Twitch.ClientSecret = "test-secret";
        });
        var channel = new ChannelKey(StreamingPlatform.Twitch, "10");
        catalog.Save(new() { Channels = [new() { Platform = channel.Platform, ChannelId = channel.ChannelId, IsConfigured = true }] });
        var auth = new TestEmbeddedAuth();
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, (key, role, context) =>
        {
            Assert.That((key, role, context), Is.EqualTo((channel, PlatformAuthRole.Bot, "tw-bot-90")));
            return new(true, "90");
        });

        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts,
            twitchOAuth: DispatchProxy.Create<ITwitchOAuthService, InertProxy>(), settingsManager: settings,
            shellLauncher: new NoOpLauncher(), embeddedTwitchAuth: auth);
        editor.Load();
        var bot = editor.VisibleChannels.Single().Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        await bot.AuthorizeCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(bot.AccountId, Is.EqualTo("90"));
            Assert.That(bot.Status, Is.EqualTo("Доступ подтверждён"));
            Assert.That(editor.ValidateAssignments(), Is.Null);
            Assert.That(accounts.ListAccounts().Single().Key.AccountId, Is.EqualTo("90"));
            Assert.That(editor.VisibleChannels.Single().ToEntry().GetAccountAssignment(PlatformAuthRole.Broadcaster), Is.Null);
        });
    }

    [Test]
    public async Task An_unresolved_channel_is_not_added_and_a_default_account_is_not_implicitly_assigned()
    {
        var accounts = new AccountsStore(filePath: Path.Combine(_root, "accounts.json"));
        accounts.Mutate(PlatformAuthRole.Bot, account =>
        {
            account.UserId = "90";
            account.Login = "thebot";
            account.AccessToken = "test-token";
        });
        var catalog = new ChannelCatalogStore(filePath: Path.Combine(_root, "channels.json"));
        catalog.Save(new() { Channels = [new() { Platform = StreamingPlatform.Twitch, ChannelId = "10", IsConfigured = true }] });
        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, twitchAccountsStore: accounts);
        editor.Load();
        editor.NewChannelInput = "https://twitch.tv/unresolved";

        await editor.AddChannelCommand.ExecuteAsync(null);
        var role = editor.VisibleChannels.Single().Roles.Single(value => value.Role == PlatformAuthRole.Bot);
        Assert.That(role.SelectedAccount, Is.Null);
        Assert.That(editor.StatusNoticeSeverity, Is.EqualTo(StatusSeverity.Error));
        role.SelectedAccount = role.Accounts.Single();
        Assert.That(editor.ValidateAssignments(), Is.Not.Null);
    }

    [Test]
    public async Task One_account_can_be_verified_for_two_channels_and_survives_reloading_the_profile()
    {
        var accountsPath = Path.Combine(_root, "accounts.json");
        var channelsPath = Path.Combine(_root, "channels.json");
        var accounts = new AccountsStore(filePath: accountsPath);
        accounts.UpsertContext(PlatformAuthRole.Bot, "shared-bot", "Бот", new() { UserId = "90", Login = "shared", AccessToken = "test-bot" });
        accounts.UpsertContext(PlatformAuthRole.Broadcaster, "same-user", "Стример", new() { UserId = "90", Login = "shared", AccessToken = "test-owner" });

        var catalog = new ChannelCatalogStore(filePath: channelsPath);
        catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "10", DisplayName = "Канал A", IsConfigured = true, BotContextId = "shared-bot", BotVerifiedSubjectId = "90" },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "20", DisplayName = "Канал B", IsConfigured = true },
            ],
        });
        var secondKey = new ChannelKey(StreamingPlatform.Twitch, "20");
        var subject = new Queue<string>(["other-id", "90"]);
        var adapter = new NullResolveAdapter(StreamingPlatform.Twitch, (key, role, context) =>
        {
            Assert.That((key, role, context), Is.EqualTo((secondKey, PlatformAuthRole.Bot, "shared-bot")));
            return new(true, subject.Dequeue());
        });

        var editor = new ChannelCatalogSettingsSectionViewModel(catalog, [adapter], twitchAccountsStore: accounts);
        editor.Load();
        var platformAccount = accounts.ListAccounts().Single();
        var firstRole = editor.VisibleChannels.Single(c => c.ChannelId == "10").Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        var secondRole = editor.VisibleChannels.Single(c => c.ChannelId == "20").Roles.Single(role => role.Role == PlatformAuthRole.Bot);
        secondRole.SelectedAccount = secondRole.Accounts.Single();
        await secondRole.VerifyCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(platformAccount.Key, Is.EqualTo(new PlatformAccountKey(StreamingPlatform.Twitch, "90")));
            Assert.That(platformAccount.Authorizations, Has.Count.EqualTo(2));
            Assert.That(firstRole.SelectedAccount?.AccountId, Is.EqualTo("90"));
            Assert.That(firstRole.Status, Is.EqualTo("Доступ подтверждён"));
            Assert.That(secondRole.Status, Does.Contain("не подтвердила"));
            Assert.That(editor.ValidateAssignments(), Does.Contain("Канал B"));
        });

        await secondRole.VerifyCommand.ExecuteAsync(null);
        Assert.That(editor.ValidateAssignments(), Is.Null);
        var settings = catalog.Load();
        editor.ApplyTo(settings);
        Assert.That(catalog.Save(settings), Is.True);

        var reopened = new ChannelCatalogStore(filePath: channelsPath);
        var bindings = reopened.Load().Channels.Select(channel => channel.GetAccountAssignment(PlatformAuthRole.Bot)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(bindings.Select(binding => binding?.Account), Is.EqualTo(new[] { platformAccount.Key, platformAccount.Key }));
            Assert.That(bindings.Select(binding => binding?.IsVerified), Is.All.True);
            Assert.That(new AccountsStore(filePath: accountsPath).Load(PlatformAuthRole.Bot, "shared-bot").AccessToken, Is.EqualTo("test-bot"));
        });
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

    private sealed class NullResolveAdapter(
        StreamingPlatform platform,
        Func<ChannelKey, PlatformAuthRole, string, ChannelAccessVerification>? verify = null) : IPlatformChannelAdapter
    {
        public StreamingPlatform Platform => platform;
        public PlatformCapabilities Capabilities => PlatformCapabilities.BackgroundStreamMonitoring;
        public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(string rawInput, string? preferredContextId, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedChannelIdentity?>(null);
        public Task<ChannelAccessVerification> VerifyChannelAccessAsync(ChannelKey channel, PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
            => Task.FromResult(verify?.Invoke(channel, role, contextId) ?? new(true, "sub"));
        public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(ChannelCatalogEntry channel, CancellationToken cancellationToken)
            => Task.FromResult(new ChannelLiveProbeResult(false, true, null));
    }

    private sealed class TestEmbeddedAuth : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request)
        {
            Assert.That((request.Role, request.CheckBroadcasterChannel), Is.EqualTo((PlatformAuthRole.Bot, false)));
            var flow = new OAuthFlowResult("fake-token", "fake-refresh", [], "somebot", "90", 3600);
            return Task.FromResult(EmbeddedTwitchAuthResult.Completed(flow));
        }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException(targetMethod?.Name);
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

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel,
            bool preservePreselectedNext, CancellationToken cancellationToken = default)
        {
            return SwitchActiveChannelAsync(platform, targetChannel, cancellationToken);
        }

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel,
            CancellationToken cancellationToken = default)
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
