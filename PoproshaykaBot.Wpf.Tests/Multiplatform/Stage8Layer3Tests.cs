using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
public sealed class Stage8Layer3Tests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-stage8-l3-" + Guid.NewGuid().ToString("N"));
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
    public async Task UserStatisticsPage_FiltersByPlatformReassignsRankingAndRoutesBalanceAdjustmentChannel()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        statistics.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "tw-1", Name = "TwitchTop", MessageCount = 50, BonusPoints = 200 },
            new() { UserId = "tw-2", Name = "TwitchSecond", MessageCount = 10, BonusPoints = 50 },
        ]);
        statistics.SeedPlatform(StreamingPlatform.YouTube,
        [
            new() { UserId = "yt-1", Name = "YouTubeLead", MessageCount = 20, BonusPoints = 100 },
        ]);

        UserRewarded? rewarded = null;
        using var sub = eventBus.Subscribe<UserRewarded>(e => rewarded = e);

        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit | PlatformCapabilities.Whisper, "twitch_chan"),
            new StubChatPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.ServerChatHistory, "UC_yt_channel"));

        using var page = new UserStatisticsPageViewModel(
            statistics,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(statistics, settingsManager, eventBus, NullLogger<UserPointsManagementService>.Instance),
            new StubChannelProvider("fallback_twitch"),
            settingsManager,
            new StubDialogService(),
            eventBus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider(),
            registry);

        page.SelectedPlatformFilter = PlatformFilterOption.AllOptions.Single(o => o.Platform == StreamingPlatform.YouTube);

        Assert.Multiple(() =>
        {
            Assert.That(page.Users, Has.Count.EqualTo(1));
            Assert.That(page.Users[0].Name, Is.EqualTo("YouTubeLead"));
            Assert.That(page.Users[0].Place, Is.EqualTo(1));
            Assert.That(page.TotalUsers, Is.EqualTo("1"));
            Assert.That(page.TotalMessages, Is.EqualTo("20"));
            Assert.That(page.TotalPoints, Is.EqualTo("120"));
        });

        page.SelectedRow = page.Users[0];
        Assert.That(page.SelectedPlaceText, Is.EqualTo("1-е место из 1"));

        page.AdjustmentText = "25";
        await page.ApplyAdjustmentCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(rewarded, Is.Not.Null);
            Assert.That(rewarded!.Channel, Is.EqualTo("UC_yt_channel"));
            Assert.That(rewarded.User, Is.EqualTo(new PlatformUserKey(StreamingPlatform.YouTube, "yt-1")));
        });
    }

    [Test]
    public void StreamHistoryPage_FiltersPlatformAndBuildsBreakdownAndChatterNote()
    {
        var twitchOnly = CreateRecord(DateTimeOffset.UtcNow.AddDays(-2), StreamingPlatform.Twitch);
        var multiPlatform = CreateRecord(DateTimeOffset.UtcNow.AddDays(-1), StreamingPlatform.Twitch, StreamingPlatform.YouTube);
        multiPlatform.Platforms[0].Chatters = [new() { UserId = "tw-user", DisplayName = "TwChatter", MessageCount = 4 }];
        multiPlatform.Platforms[1].Chatters = [new() { UserId = "yt-user", DisplayName = "YtChatter", MessageCount = 7 }];

        var store = new BroadcastHistoryStore(
            null,
            Path.Combine(_root, "broadcasts.json"),
            Path.Combine(_root, "streams.json"));
        store.TryAppend(twitchOnly);
        store.TryAppend(multiPlatform);

        using var page = new StreamHistoryPageViewModel(
            store,
            new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance),
            new MemorySettings(),
            new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));

        Assert.That(page.Sessions, Has.Count.EqualTo(2));

        page.SelectedPlatformFilter = PlatformFilterOption.AllOptions.Single(o => o.Platform == StreamingPlatform.YouTube);
        Assert.Multiple(() =>
        {
            Assert.That(page.Sessions, Has.Count.EqualTo(1));
            Assert.That(page.Sessions[0].Source.Id, Is.EqualTo(multiPlatform.Id));
            Assert.That(page.Sessions[0].Platforms, Is.EqualTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }));
        });

        page.SelectedRow = page.Sessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(page.HasDetailPlatformBreakdown, Is.True);
            Assert.That(page.DetailPlatformBreakdown, Has.Count.EqualTo(2));
            Assert.That(page.HasDetailChatterSumNote, Is.True);
            Assert.That(page.DetailChatterSumNote, Contains.Substring("Сумма уникальных авторов"));
            Assert.That(page.Chatters.Select(c => c.Key.Platform), Is.EqualTo(new[] { StreamingPlatform.YouTube, StreamingPlatform.Twitch }));
        });

        page.SelectedPlatformFilter = PlatformFilterOption.All;
        page.SelectedRow = page.Sessions.Single(s => s.Source.Id == twitchOnly.Id);
        Assert.Multiple(() =>
        {
            Assert.That(page.HasDetailPlatformBreakdown, Is.False);
            Assert.That(page.DetailPlatformBreakdown, Is.Empty);
            Assert.That(page.HasDetailChatterSumNote, Is.False);
            Assert.That(page.IsFilteredEmpty, Is.False);
        });

        page.SetSessionHiddenCommand.Execute(page.Sessions.Single(s => s.Source.Id == multiPlatform.Id));
        page.SelectedPlatformFilter = PlatformFilterOption.AllOptions.Single(o => o.Platform == StreamingPlatform.YouTube);
        Assert.Multiple(() =>
        {
            Assert.That(page.HasSessions, Is.True);
            Assert.That(page.IsFilteredEmpty, Is.True);
        });

        page.ClearAllFiltersCommand.Execute(null);
        Assert.That(page.IsFilteredEmpty, Is.False);
    }

    [Test]
    public void BroadcastProfileEditDialog_DefaultsToTwitchOnlyAndTogglesCurrentSettingsAvailability()
    {
        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, "tw"),
            new StubChatPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.ServerChatHistory, "yt"));

        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var dialog = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance,
            registry);

        dialog.LoadFrom(new BroadcastProfile { Name = "Утро" });
        Assert.Multiple(() =>
        {
            Assert.That(dialog.TargetPlatforms.Select(p => p.Platform), Is.EqualTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }));
            Assert.That(dialog.GetSelectedPlatforms(), Is.EqualTo(new[] { StreamingPlatform.Twitch }));
            Assert.That(dialog.IsPlatformsSelectionEnabled, Is.True);
        });

        dialog.LoadFrom(new BroadcastProfile { Name = "Два" }, new HashSet<StreamingPlatform> { StreamingPlatform.Twitch, StreamingPlatform.YouTube });
        Assert.That(dialog.GetSelectedPlatforms(), Is.EquivalentTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }));
        Assert.That(dialog.ConfirmCommand.CanExecute(null), Is.True);

        foreach (var option in dialog.TargetPlatforms)
        {
            option.IsSelected = false;
        }
        Assert.That(dialog.ConfirmCommand.CanExecute(null), Is.False);

        dialog.ConfigureCurrentSettingsMode();
        Assert.That(dialog.IsPlatformsSelectionEnabled, Is.False);

        dialog.Name = "Новый профиль";
        Assert.That(dialog.IsPlatformsSelectionEnabled, Is.True);
        Assert.That(dialog.ConfirmCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void CommandsPage_SwitchesProbePlatformToYouTubeDisablesMetadataCommandsAndHidesWhisperNotice()
    {
        var registry = new StubPlatformRegistry(
            new StubChatPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit | PlatformCapabilities.Whisper, "tw"),
            new StubChatPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.ServerChatHistory, "yt"));

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"));
        var cmdStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));
        cmdStore.Mutate(s => s.DefaultResponseTarget = CommandResponseTarget.Whisper | CommandResponseTarget.Chat);

        var metadataCmd = new StubCommand("title", PlatformCapabilities.ChannelMetadataEdit);
        var generalCmd = new StubCommand("помощь", PlatformCapabilities.None);
        var usage = new CommandUsageRepository(TimeProvider.System);
        var processor = new ChatCommandProcessor(
            [metadataCmd, generalCmd],
            cmdStore,
            usage,
            NullLogger<ChatCommandProcessor>.Instance,
            "!");

        var page = new CommandsPageViewModel(
            processor,
            cmdStore,
            settingsManager,
            usage,
            new StubPrompt(),
            new AccountsStore(null, Path.Combine(_root, "accounts.json")),
            NullLogger<CommandsPageViewModel>.Instance,
            registry);

        var titleRow = page.Rows.Single(r => r.Canonical == "title");
        var helpRow = page.Rows.Single(r => r.Canonical == "помощь");

        Assert.Multiple(() =>
        {
            Assert.That(page.IsPlatformScope, Is.False);
            Assert.That(titleRow.IsClosedByCode, Is.False);
            Assert.That(helpRow.IsClosedByCode, Is.False);
            Assert.That(page.WhisperNotice, Is.Not.Null);
        });

        page.CommandScope = page.CommandScopes.Single(scope => scope.Platform == StreamingPlatform.YouTube);

        Assert.Multiple(() =>
        {
            Assert.That(page.CommandScope.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(titleRow.IsClosedByCode, Is.True);
            Assert.That(titleRow.AccessText, Contains.Substring("YouTube"));
            Assert.That(helpRow.IsClosedByCode, Is.False);
            Assert.That(page.WhisperNotice, Is.Null);
        });

        Assert.That(page.TrySelect("title"), Is.True);
        Assert.That(page.CanSetPlatformEnablement, Is.False);

        Assert.That(page.TrySelect("помощь"), Is.True);
        Assert.That(page.SelectedPlatformEnablement.Enabled, Is.Null);
        page.SelectedPlatformEnablement = page.PlatformEnablementOptions.Single(option => option.Enabled == false);
        Assert.That(new CommandSettingsStore(null, Path.Combine(_root, "commands.json"))
            .Load().IsEnabled("помощь", StreamingPlatform.YouTube), Is.False);
        Assert.That(cmdStore.Load().IsEnabled("помощь", StreamingPlatform.Twitch), Is.True);
        Assert.That(helpRow.IsEnabled, Is.True);
        Assert.That(helpRow.IsEnabledInScope, Is.False);
        Assert.That(helpRow.Summary, Does.StartWith("!помощь, выключена"));
        Assert.That(page.SummaryLine, Does.StartWith("Включено 1 из 2"));

        page.SelectedPlatformEnablement = page.PlatformEnablementOptions.Single(option => option.Enabled is null);
        Assert.That(cmdStore.Load().Commands.ContainsKey("помощь"), Is.False);
        Assert.That(helpRow.IsEnabledInScope, Is.True);
    }

    private static BroadcastRecord CreateRecord(DateTimeOffset started, params StreamingPlatform[] platforms)
    {
        return new BroadcastRecord
        {
            StartedAt = started,
            EndedAt = started.AddHours(2),
            Platforms = platforms.Select(p => new BroadcastPlatformRecord
            {
                Platform = p,
                Channel = p == StreamingPlatform.Twitch ? "tw_chan" : "UC_yt",
                StartedAt = started,
                EndedAt = started.AddHours(2),
                MessageCount = 5,
                ChatterCount = 1,
            }).ToList(),
        };
    }

    private sealed class StubChatPlatform(
        StreamingPlatform id,
        string displayName,
        PlatformCapabilities capabilities,
        string? configuredChannel) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => capabilities;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus => null!;
        public IPlatformMessenger Messenger => null!;
        public string? ConfiguredChannel => configuredChannel;
        public string? BuildStreamUrl(StreamInfo? stream = null) => null;
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
        public string? GetConfiguredChannel(StreamingPlatform platform) => Find(platform)?.ConfiguredChannel;
        public string? BuildStreamUrl(StreamingPlatform platform, StreamInfo? stream = null) => null;
    }

    private sealed class StubChannelProvider(string channel) : IChannelProvider
    {
        public string? Channel => channel;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class StubGameResolver : IGameCategoryResolver
    {
        public Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GameSuggestion>>([]);

        public Task<GameCategoryResolution> ResolveAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(new GameCategoryResolution(null, []));

        public Task RememberAsync(GameSuggestion suggestion) => Task.CompletedTask;
    }

    private sealed class StubCommand(string canonical, PlatformCapabilities required) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public PlatformCapabilities RequiredCapabilities => required;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
            => Task.FromResult<OutgoingMessage?>(null);
    }

    private sealed class StubPrompt : IUnsavedChangesPrompt
    {
        public UnsavedChangesDecision Ask(string subject, string action) => UnsavedChangesDecision.Discard;
    }
}
