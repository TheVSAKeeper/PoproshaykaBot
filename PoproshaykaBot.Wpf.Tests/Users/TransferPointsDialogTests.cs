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
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
public sealed class TransferPointsDialogTests
{
    private string _root = null!;

    private static readonly ChannelKey TwitchMain = new(StreamingPlatform.Twitch, "tw_main");
    private static readonly ChannelKey TwitchAlt = new(StreamingPlatform.Twitch, "tw_alt");
    private static readonly ChannelKey YouTubeMain = new(StreamingPlatform.YouTube, "UC_yt_main");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"poproshayka-wpf-transfer-{Guid.NewGuid():N}");
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
    public async Task Dialog_preview_filters_manual_selection_and_excludes_same_target()
    {
        var gate = new SettingsWriteGate();
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        catalog.Save(new ChannelsSettings
        {
            Channels =
            [
                new() { Platform = TwitchMain.Platform, ChannelId = TwitchMain.ChannelId, DisplayName = "Основной Twitch" },
                new() { Platform = TwitchAlt.Platform, ChannelId = TwitchAlt.ChannelId, DisplayName = "Второй Twitch" },
                new() { Platform = YouTubeMain.Platform, ChannelId = YouTubeMain.ChannelId, DisplayName = "YouTube" },
            ],
        });

        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var transferService = new PointsTransferService(repo, bus);
        var dialogs = new StubDialogService { ConfirmWarningResult = true };

        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        var u2 = new PlatformUserKey(StreamingPlatform.Twitch, "u2");
        var yt1 = new PlatformUserKey(StreamingPlatform.YouTube, "yt1");

        repo.SeedChannel(TwitchMain,
        [
            new() { UserId = "u1", Name = "Alice", MessageCount = 5, BonusPoints = 10 },
            new() { UserId = "u2", Name = "Bob", MessageCount = 3, BonusPoints = 2 },
        ]);
        repo.SeedChannel(YouTubeMain,
        [
            new() { UserId = "yt1", Name = "AliceYT", MessageCount = 1 },
        ]);
        repo.LinkAccounts([u1, yt1], AccountLinkOrigin.StreamerManual, new(AccountLinkAuthorKind.Streamer, ActorDisplayName: "host"), true, out _);

        var dialog = new TransferPointsDialogViewModel(
            repo,
            transferService,
            catalog,
            new PointTerm(),
            dialogs,
            initialSource: PointsTransferSource.ForChannel(TwitchMain),
            filteredUserKeys: [u1],
            initialSelectedUserKeys: [u1],
            hasPageFilter: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.SelectedSource?.Source, Is.EqualTo(PointsTransferSource.ForChannel(TwitchMain)));
            Assert.That(dialog.TargetOptions.Any(t => t.Channel == TwitchMain), Is.False);
            Assert.That(dialog.TransferredCount, Is.EqualTo(2));
            Assert.That(dialog.CanUseFilteredScope, Is.True);
        }

        dialog.SelectedTarget = dialog.TargetOptions.Single(t => t.Channel == YouTubeMain);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.IsCrossPlatform, Is.True);
            Assert.That(dialog.TransferredCount, Is.EqualTo(1));
            Assert.That(dialog.SkippedCount, Is.EqualTo(1));
            Assert.That(dialog.PreviewUsers.Single(u => u.SourceUserKey == u1).StatusText, Is.EqualTo("Перенесётся"));
            Assert.That(dialog.PreviewUsers.Single(u => u.SourceUserKey == u2).StatusText, Is.EqualTo("Пропущен: нет связанного аккаунта"));
        }

        dialog.SelectedTarget = dialog.TargetOptions.Single(t => t.Channel == TwitchAlt);
        dialog.IsScopeFiltered = true;
        Assert.That(dialog.TransferredCount, Is.EqualTo(1));

        dialog.IsScopeSelected = true;
        Assert.That(dialog.TransferredCount, Is.EqualTo(1));
        dialog.SelectAllUsersCommand.Execute(null);
        Assert.That(dialog.TransferredCount, Is.EqualTo(2));

        dialogs.ConfirmWarningResult = false;
        await dialog.ExecuteTransferCommand.ExecuteAsync(null);
        Assert.That(dialog.HasCompleted, Is.False);
        Assert.That(repo.QuerySlice(StatisticsSlice.ForChannel(TwitchAlt)), Is.Empty);

        dialogs.ConfirmWarningResult = true;
        dialog.IsModeAdjustmentsOnly = true;
        await dialog.ExecuteTransferCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.HasCompleted, Is.True);
            Assert.That(dialog.ExecutionResult?.Result.TransferredCount, Is.EqualTo(2));
            Assert.That(dialog.ExecuteTransferCommand.CanExecute(null), Is.False);
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchAlt), u1)?.BonusPoints, Is.EqualTo(10UL));
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1)?.MessageCount, Is.EqualTo(5UL));
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1)?.BonusPoints, Is.EqualTo(0UL));
        }
    }

    [Test]
    public async Task Users_page_TransferPointsCommand_prefills_unattributed_slice_reloads_and_reports_status()
    {
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"), gate);
        var catalog = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        catalog.Save(new ChannelsSettings
        {
            Channels =
            [
                new() { Platform = TwitchMain.Platform, ChannelId = TwitchMain.ChannelId, DisplayName = "Основной Twitch" },
            ],
        });

        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var transferService = new PointsTransferService(repo, bus);
        var dialogs = new StubDialogService
        {
            ConfirmWarningResult = true,
            OnShowAsync = async vm =>
            {
                if (vm is TransferPointsDialogViewModel transferVm)
                {
                    Assert.That(transferVm.SelectedSource?.Source, Is.EqualTo(PointsTransferSource.ForUnattributed(StreamingPlatform.Twitch)));
                    transferVm.SelectedTarget = transferVm.TargetOptions.Single(t => t.Channel == TwitchMain);
                    await transferVm.ExecuteTransferCommand.ExecuteAsync(null);
                    transferVm.CloseCommand.Execute(null);
                    return true;
                }

                return false;
            },
        };

        repo.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "legacy_1", Name = "LegacyAlice", MessageCount = 4, BonusPoints = 6 },
        ]);

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
            accountLinking: new AccountLinkingService(repo, bus),
            pointsTransfer: transferService);

        string? statusMessage = null;
        page.StatusReported += (msg, _) => statusMessage = msg;

        page.SelectedSliceOption = page.SliceOptions.Single(o => o.Slice.Kind == StatisticsSliceKind.UnattributedPlatform);
        Assert.That(page.TrySelect("legacy_1"), Is.True);

        await page.TransferPointsCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.SliceOptions.Any(o => o.Slice.Kind == StatisticsSliceKind.UnattributedPlatform), Is.False);
            Assert.That(page.SelectedSliceOption.Slice.Kind, Is.EqualTo(StatisticsSliceKind.AllChannels));
            Assert.That(page.Users, Has.Count.EqualTo(1));
            Assert.That(page.SelectedRow?.Points, Is.EqualTo(10L));
            Assert.That(statusMessage, Does.Contain("Перенос баллов завершён"));
        }
    }

    [Test]
    public async Task Dialog_implements_ICancelableDialog_and_locks_confirmed_user_set()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var transferService = new PointsTransferService(repo, bus);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        var u2 = new PlatformUserKey(StreamingPlatform.Twitch, "u2");
        var u3 = new PlatformUserKey(StreamingPlatform.Twitch, "u3");

        repo.TrackMessage(TwitchMain, u1, "Alice");
        repo.TrackMessage(TwitchMain, u2, "Bob");

        var dialogs = new StubDialogService
        {
            OnConfirmWarning = () =>
            {
                repo.TrackMessage(TwitchMain, u3, "Latecomer");
                return true;
            },
        };

        var vm = new TransferPointsDialogViewModel(
            repo,
            transferService,
            null,
            new PointTerm(),
            dialogs,
            PointsTransferSource.ForChannel(TwitchMain));

        vm.TargetOptions.Add(new(TwitchAlt, "Twitch · Alt"));
        vm.SelectedTarget = vm.TargetOptions.Single(t => t.Channel == TwitchAlt);

        Assert.That(vm, Is.InstanceOf<ICancelableDialog>());
        Assert.That(((ICancelableDialog)vm).CanCancel, Is.True);

        await vm.ExecuteTransferCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.ExecutionResult?.Result.TransferredCount, Is.EqualTo(2));
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchAlt), u3), Is.Null);
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u3)?.MessageCount, Is.EqualTo(1UL));
        }
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "tw_main";
    }

    private sealed class StubDialogService : IDialogService
    {
        public bool ConfirmWarningResult { get; set; } = true;
        public Func<bool>? OnConfirmWarning { get; set; }
        public Func<IDialogViewModel, Task<bool>>? OnShowAsync { get; set; }

        public Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            return OnShowAsync is not null ? OnShowAsync(viewModel) : Task.FromResult(false);
        }

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) =>
            OnConfirmWarning is not null ? OnConfirmWarning() : ConfirmWarningResult;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }
}
