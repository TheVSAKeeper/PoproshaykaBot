using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
public sealed class UserStatisticsChannelSlicesTests
{
    private string _root = null!;

    private static readonly ChannelKey TwitchMain = new(StreamingPlatform.Twitch, "1001");
    private static readonly ChannelKey YouTubeMain = new(StreamingPlatform.YouTube, "UC_yt_1");
    private static readonly ChannelKey YouTubeSecond = new(StreamingPlatform.YouTube, "UC_yt_2");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-wpf-slices-{Guid.NewGuid():N}");
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
    public void Slice_selector_exposes_All_channels_groups_and_unattributed_buckets()
    {
        var (repo, catalog, linking, page, _) = CreatePage(seedUnattributed: true);
        using (page)
        {
            var labels = page.SliceOptions.Select(static o => o.Label).ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(labels, Does.Contain("Все каналы"));
                Assert.That(labels, Does.Contain("Twitch · tw_main"));
                Assert.That(labels, Does.Contain("YouTube · Канал A"));
                Assert.That(labels, Does.Contain("Группа: Основные"));
                Assert.That(labels, Does.Contain("Twitch · старые данные без канала"));
            }

            page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Kind == StatisticsSliceKind.Channel && o.Slice.Channel == YouTubeMain);
            Assert.That(page.Users.Select(static u => u.MessageCount), Is.EqualTo(new[] { 6UL }));

            page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Kind == StatisticsSliceKind.ChannelGroup);
            Assert.That(page.Users.Sum(static u => (long)u.MessageCount), Is.EqualTo(10L));

            page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Kind == StatisticsSliceKind.UnattributedPlatform);
            Assert.That(page.Users, Has.Count.EqualTo(1));
            Assert.That(page.Users[0].Name, Is.EqualTo("LegacyUser"));

            catalog.Mutate(s => s.Groups.Add(new()
            {
                Id = "g2",
                Name = "Новые",
                Channels = [new() { Platform = YouTubeSecond.Platform, ChannelId = YouTubeSecond.ChannelId }],
            }));

            Assert.That(page.SliceOptions.Select(static o => o.Label), Does.Contain("Группа: Новые"));
        }
    }

    [Test]
    public async Task Link_and_unlink_via_Users_page_merge_and_separate_rows_without_losing_counters()
    {
        var twAlice = new PlatformUserKey(StreamingPlatform.Twitch, "tw_alice");
        var ytAlice = new PlatformUserKey(StreamingPlatform.YouTube, "yt_alice");
        var (repo, _, _, page, dialogs) = CreatePage(seedUnattributed: false);
        using (page)
        {
            dialogs.NextLinkTarget = ytAlice;

            Assert.That(page.TrySelect(twAlice), Is.True);
            Assert.That(page.Users, Has.Count.EqualTo(2));

            await page.LinkAccountCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.Users, Has.Count.EqualTo(1));
                Assert.That(page.Users[0].IsLinked, Is.True);
                Assert.That(page.Users[0].HasChannelBreakdowns, Is.True);
                Assert.That(page.Users[0].ChannelBreakdowns, Has.Count.EqualTo(2));
                Assert.That(page.HasMultipleAdjustmentTargets, Is.True);
                Assert.That(page.TrySelect(ytAlice), Is.True, "TrySelect должен находить объединённую строку по любому связанному аккаунту");
            }

            var linkedYtRow = page.SelectedRow!.LinkedAccounts.Single(a => a.Key == ytAlice);
            await page.UnlinkAccountCommand.ExecuteAsync(linkedYtRow);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.Users, Has.Count.EqualTo(2));
                Assert.That(repo.GetLinkedKeys(twAlice), Is.EquivalentTo(new[] { twAlice }));
                Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), twAlice)!.MessageCount, Is.EqualTo(4UL));
                Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(YouTubeMain), ytAlice)!.MessageCount, Is.EqualTo(6UL));
            }
        }
    }

    [Test]
    public async Task ChannelGroups_contributor_saves_groups_and_respects_SettingsWriteGate()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        catalog.EnsureChannelKnown(TwitchMain, "tw_main");
        catalog.EnsureChannelKnown(YouTubeMain, "Канал A");

        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var catalogVm = new ChannelCatalogSettingsSectionViewModel(catalog);
        var vm = new ChannelGroupsSettingsSectionViewModel(catalog, repo);
        var contributor = new ChannelsSettingsSectionContributor(catalogVm, vm, catalog);

        contributor.Load(new SettingsSessionState { Settings = new() });
        using var dirtySub = contributor.SubscribeDirty(new SettingsSessionState { Settings = new() }, static () => { }, static () => { });

        vm.NewGroupName = "Стримы";
        vm.AddGroupCommand.Execute(null);
        vm.SelectedGroup!.Channels[0].IsSelected = true;

        Assert.That(contributor.ValidateBeforeSave(), Is.Null);
        var saved = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.That(saved.Written, Is.True);
        Assert.That(catalog.FindGroup("Стримы")?.Channels, Has.Count.EqualTo(1));

        await gate.RunExternalWriteAsync(() => Task.FromResult(0), static _ => ["channels.json"]);

        vm.NewGroupName = "Заблокированная";
        vm.AddGroupCommand.Execute(null);
        var blocked = await contributor.SaveAsync(new(), appSettingsWritten: true);
        Assert.That(blocked.Written, Is.False);
    }

    [Test]
    public void Adjustment_default_prefers_most_recent_LastSeen_channel_and_ChannelGroups_includes_history_channels()
    {
        var time = new ManualTimeProvider();
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings-f10.json"), gate);
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels-f10.json"), gate);
        catalog.EnsureChannelKnown(TwitchMain, "tw_main");
        catalog.EnsureChannelKnown(YouTubeMain, "Канал A");

        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance, time);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var linking = new AccountLinkingService(repo, bus, time);
        var dialogs = new LinkDialogStub();

        var twBob = new PlatformUserKey(StreamingPlatform.Twitch, "tw_bob");
        var ytBob = new PlatformUserKey(StreamingPlatform.YouTube, "yt_bob");
        repo.TrackMessage(TwitchMain, twBob, "BobTw");
        repo.IncrementBonusPoints(StatisticsCounterTarget.ForChannel(TwitchMain), twBob, 500, "BobTw");
        time.Advance(TimeSpan.FromHours(2));
        repo.TrackMessage(YouTubeMain, ytBob, "BobYt");
        repo.IncrementBonusPoints(StatisticsCounterTarget.ForChannel(YouTubeMain), ytBob, 10, "BobYt");
        repo.LinkAccounts([twBob, ytBob], AccountLinkOrigin.StreamerManual, new(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "b", "Streamer"), allowMergeExistingGroups: true, out _);

        using var page = new UserStatisticsPageViewModel(
            repo,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(repo, settingsManager, bus, NullLogger<UserPointsManagementService>.Instance),
            new FakeChannelProvider(),
            settingsManager,
            dialogs,
            bus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider(),
            channelCatalog: catalog,
            accountLinking: linking);
        // Switch to TwitchMain where ytBob wouldn't appear individually, then TrySelect(ytBob) to verify cross-slice reset & default target
        page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Kind == StatisticsSliceKind.AllChannels);
        page.SelectedRow = page.Users.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.SelectedAdjustmentTarget?.Target.AsChannelKey, Is.EqualTo(YouTubeMain));
            page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Channel == YouTubeMain);
            Assert.That(page.TrySelect(twBob), Is.True);
        }

        var historyStore = new BroadcastHistoryStore(
            NullLogger<BroadcastHistoryStore>.Instance,
            Path.Combine(_root, "broadcasts-f12.json"),
            Path.Combine(_root, "stream_sessions-f12.json"));
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = time.GetUtcNow(),
            EndedAt = time.GetUtcNow().AddHours(1),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = YouTubeSecond.Platform,
                    Channel = "Канал из истории",
                    ChannelId = YouTubeSecond.ChannelId,
                    StartedAt = time.GetUtcNow(),
                    EndedAt = time.GetUtcNow().AddHours(1),
                },
            ],
        });

        var groupsVm = new ChannelGroupsSettingsSectionViewModel(catalog, repo, historyStore);
        groupsVm.Load();
        groupsVm.NewGroupName = "Все каналы";
        groupsVm.AddGroupCommand.Execute(null);

        Assert.That(groupsVm.SelectedGroup!.Channels.Any(c => c.Key == YouTubeSecond && c.Label.Contains("Канал из истории")), Is.True);
    }

    private (
        UserStatisticsRepository Repo,
        ChannelCatalogStore Catalog,
        AccountLinkingService Linking,
        UserStatisticsPageViewModel Page,
        LinkDialogStub Dialogs) CreatePage(bool seedUnattributed)
    {
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"), gate);
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var linking = new AccountLinkingService(repo, bus);
        var dialogs = new LinkDialogStub();

        catalog.Save(new ChannelsSettings
        {
            Channels =
            [
                new() { Platform = TwitchMain.Platform, ChannelId = TwitchMain.ChannelId, DisplayName = "tw_main" },
                new() { Platform = YouTubeMain.Platform, ChannelId = YouTubeMain.ChannelId, DisplayName = "Канал A" },
                new() { Platform = YouTubeSecond.Platform, ChannelId = YouTubeSecond.ChannelId, DisplayName = "Канал B" },
            ],
            Groups =
            [
                new()
                {
                    Id = "main",
                    Name = "Основные",
                    Channels =
                    [
                        new() { Platform = TwitchMain.Platform, ChannelId = TwitchMain.ChannelId },
                        new() { Platform = YouTubeMain.Platform, ChannelId = YouTubeMain.ChannelId },
                    ],
                },
            ],
        });

        if (seedUnattributed)
        {
            repo.SeedPlatform(StreamingPlatform.Twitch,
            [
                new() { UserId = "legacy_1", Name = "LegacyUser", MessageCount = 3 },
            ]);
        }

        var twAlice = new PlatformUserKey(StreamingPlatform.Twitch, "tw_alice");
        var ytAlice = new PlatformUserKey(StreamingPlatform.YouTube, "yt_alice");
        for (var i = 0; i < 4; i++)
        {
            repo.TrackMessage(TwitchMain, twAlice, "AliceTw");
        }

        for (var i = 0; i < 6; i++)
        {
            repo.TrackMessage(YouTubeMain, ytAlice, "AliceYt");
        }

        var page = new UserStatisticsPageViewModel(
            repo,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(repo, settingsManager, bus, NullLogger<UserPointsManagementService>.Instance),
            new FakeChannelProvider(),
            settingsManager,
            dialogs,
            bus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider(),
            channelCatalog: catalog,
            accountLinking: linking);

        return (repo, catalog, linking, page, dialogs);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "tw_main";
    }

    private sealed class LinkDialogStub : IDialogService
    {
        public PlatformUserKey? NextLinkTarget { get; set; }

        public Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            if (viewModel is LinkAccountDialogViewModel linkVm && NextLinkTarget is { } target)
            {
                linkVm.SelectedCandidate = linkVm.FilteredCandidates.Single(c => c.Key == target);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }
}
