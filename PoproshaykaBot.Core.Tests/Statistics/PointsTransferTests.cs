using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class PointsTransferTests
{
    private string _directory = null!;

    private static readonly ChannelKey TwitchMain = new(StreamingPlatform.Twitch, "tw_main");
    private static readonly ChannelKey TwitchSecond = new(StreamingPlatform.Twitch, "tw_second");
    private static readonly ChannelKey YouTubeMain = new(StreamingPlatform.YouTube, "UC_yt_main");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-transfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Unattributed_to_channel_AllCounters_transfers_all_and_removes_empty_unattributed_bucket()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        var u2 = new PlatformUserKey(StreamingPlatform.Twitch, "u2");

        repo.SeedPlatform(StreamingPlatform.Twitch,
        [
            new()
            {
                UserId = "u1",
                Name = "Alice",
                MessageCount = 10,
                BonusPoints = 5,
                PenaltyPoints = 2,
                FirstSeen = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeen = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            },
            new()
            {
                UserId = "u2",
                Name = "Bob",
                MessageCount = 4,
                BonusPoints = 1,
                PenaltyPoints = 0,
                FirstSeen = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeen = new DateTime(2025, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            },
        ]);

        var beforeTotal = repo.QuerySlice(StatisticsSlice.All()).Sum(static x => x.Points);
        var source = PointsTransferSource.ForUnattributed(StreamingPlatform.Twitch);
        var request = new PointsTransferRequest(source, TwitchMain, PointsTransferMode.AllCounters);

        var preview = repo.PreviewTransfer(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview.TransferredCount, Is.EqualTo(2));
            Assert.That(preview.SkippedCount, Is.EqualTo(0));
            Assert.That(preview.TotalMessages, Is.EqualTo(14UL));
            Assert.That(preview.TotalBonusPoints, Is.EqualTo(6UL));
            Assert.That(preview.TotalPenaltyPoints, Is.EqualTo(2UL));
            Assert.That(preview.TotalPoints, Is.EqualTo(18L));
            Assert.That(repo.HasChanges, Is.False);
            Assert.That(repo.GetUnattributedPlatforms(), Is.EqualTo(new[] { StreamingPlatform.Twitch }));
        }

        var result = repo.ExecuteTransfer(request);
        var afterTotal = repo.QuerySlice(StatisticsSlice.All()).Sum(static x => x.Points);
        var alice = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1)!;
        var bob = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u2)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TransferredCount, Is.EqualTo(2));
            Assert.That(result.TotalPoints, Is.EqualTo(18L));
            Assert.That(afterTotal, Is.EqualTo(beforeTotal));
            Assert.That(repo.GetUnattributedPlatforms(), Is.Empty);
            Assert.That(repo.QuerySlice(StatisticsSlice.Unattributed(StreamingPlatform.Twitch)), Is.Empty);
            Assert.That(alice.MessageCount, Is.EqualTo(10UL));
            Assert.That(alice.BonusPoints, Is.EqualTo(5UL));
            Assert.That(alice.PenaltyPoints, Is.EqualTo(2UL));
            Assert.That(alice.FirstSeen, Is.EqualTo(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(alice.LastSeen, Is.EqualTo(new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(bob.Points, Is.EqualTo(5L));
            Assert.That(repo.HasChanges, Is.True);
        }
    }

    [Test]
    public void Unattributed_subset_and_AdjustmentsOnly_preserve_unselected_and_messages()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        var u2 = new PlatformUserKey(StreamingPlatform.Twitch, "u2");
        var u3 = new PlatformUserKey(StreamingPlatform.Twitch, "u3");

        repo.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "u1", Name = "Alice", MessageCount = 10, BonusPoints = 20, PenaltyPoints = 5 },
            new() { UserId = "u2", Name = "Bob", MessageCount = 0, BonusPoints = 15, PenaltyPoints = 0 },
            new() { UserId = "u3", Name = "Charlie", MessageCount = 7, BonusPoints = 0, PenaltyPoints = 0 },
        ]);

        var source = PointsTransferSource.ForUnattributed(StreamingPlatform.Twitch);
        var subsetRequest = new PointsTransferRequest(source, TwitchMain, PointsTransferMode.AdjustmentsOnly, [u1, u2, u3]);

        var result = repo.ExecuteTransfer(subsetRequest);

        var u1Source = repo.FindViewerById(StatisticsSlice.Unattributed(StreamingPlatform.Twitch), u1);
        var u2Source = repo.FindViewerById(StatisticsSlice.Unattributed(StreamingPlatform.Twitch), u2);
        var u3Source = repo.FindViewerById(StatisticsSlice.Unattributed(StreamingPlatform.Twitch), u3);
        var u1Target = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1);
        var u2Target = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u2);
        var u3Target = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TransferredCount, Is.EqualTo(2));
            Assert.That(result.SkippedCount, Is.EqualTo(1));
            Assert.That(result.TotalMessages, Is.EqualTo(0UL));
            Assert.That(result.TotalBonusPoints, Is.EqualTo(35UL));
            Assert.That(result.TotalPenaltyPoints, Is.EqualTo(5UL));
            Assert.That(result.TotalPoints, Is.EqualTo(30L));

            Assert.That(u1Source, Is.Not.Null);
            Assert.That(u1Source!.MessageCount, Is.EqualTo(10UL));
            Assert.That(u1Source.BonusPoints, Is.EqualTo(0UL));
            Assert.That(u1Source.PenaltyPoints, Is.EqualTo(0UL));

            Assert.That(u2Source, Is.Null);
            Assert.That(u3Source!.MessageCount, Is.EqualTo(7UL));

            Assert.That(u1Target!.MessageCount, Is.EqualTo(0UL));
            Assert.That(u1Target.BonusPoints, Is.EqualTo(20UL));
            Assert.That(u1Target.PenaltyPoints, Is.EqualTo(5UL));
            Assert.That(u2Target!.BonusPoints, Is.EqualTo(15UL));
            Assert.That(u3Target, Is.Null);
        }
    }

    [Test]
    public void Channel_to_channel_merges_existing_counters_dates_and_saturates_ulong()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var viewer = new PlatformUserKey(StreamingPlatform.Twitch, "v1");

        repo.SeedChannel(TwitchMain,
        [
            new()
            {
                UserId = "v1",
                Name = "Viewer",
                MessageCount = 8,
                BonusPoints = ulong.MaxValue - 3,
                PenaltyPoints = 4,
                FirstSeen = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastSeen = new DateTime(2025, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            },
        ]);

        repo.SeedChannel(TwitchSecond,
        [
            new()
            {
                UserId = "v1",
                Name = "Viewer",
                MessageCount = 5,
                BonusPoints = 10,
                PenaltyPoints = 2,
                FirstSeen = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
                LastSeen = new DateTime(2025, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            },
        ]);

        var result = repo.ExecuteTransfer(new(
            PointsTransferSource.ForChannel(TwitchMain),
            TwitchSecond,
            PointsTransferMode.AllCounters));

        var merged = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchSecond), viewer)!;
        var sourceLeftover = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), viewer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TransferredCount, Is.EqualTo(1));
            Assert.That(sourceLeftover, Is.Not.Null);
            Assert.That(sourceLeftover!.MessageCount, Is.EqualTo(0UL));
            Assert.That(sourceLeftover.BonusPoints, Is.EqualTo(7UL));
            Assert.That(sourceLeftover.PenaltyPoints, Is.EqualTo(0UL));
            Assert.That(merged.MessageCount, Is.EqualTo(13UL));
            Assert.That(merged.BonusPoints, Is.EqualTo(ulong.MaxValue));
            Assert.That(merged.PenaltyPoints, Is.EqualTo(6UL));
            Assert.That(merged.FirstSeen, Is.EqualTo(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(merged.LastSeen, Is.EqualTo(new DateTime(2025, 1, 20, 0, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public void Cross_platform_transfer_uses_unique_link_and_skips_unlinked_or_ambiguous_without_touching_source()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var author = new AccountLinkAuthor(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "host", "Host");

        var twLinked = new PlatformUserKey(StreamingPlatform.Twitch, "tw_linked");
        var ytLinked = new PlatformUserKey(StreamingPlatform.YouTube, "yt_linked");

        var twUnlinked = new PlatformUserKey(StreamingPlatform.Twitch, "tw_unlinked");

        var twAmbiguous = new PlatformUserKey(StreamingPlatform.Twitch, "tw_ambig");
        var ytAmbig1 = new PlatformUserKey(StreamingPlatform.YouTube, "yt_a1");
        var ytAmbig2 = new PlatformUserKey(StreamingPlatform.YouTube, "yt_a2");

        repo.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "tw_linked", Name = "LinkedTw", MessageCount = 6, BonusPoints = 4 },
            new() { UserId = "tw_unlinked", Name = "UnlinkedTw", MessageCount = 5, BonusPoints = 2 },
            new() { UserId = "tw_ambig", Name = "AmbiguousTw", MessageCount = 3, BonusPoints = 1 },
        ]);
        repo.SeedChannel(YouTubeMain,
        [
            new() { UserId = "yt_linked", Name = "LinkedYt", MessageCount = 2, BonusPoints = 1 },
            new() { UserId = "yt_a1", Name = "AmbigYt1", MessageCount = 1 },
            new() { UserId = "yt_a2", Name = "AmbigYt2", MessageCount = 1 },
        ]);

        repo.LinkAccounts([twLinked, ytLinked], AccountLinkOrigin.StreamerManual, author, allowMergeExistingGroups: true, out _);
        repo.LinkAccounts([twAmbiguous, ytAmbig1, ytAmbig2], AccountLinkOrigin.StreamerManual, author, allowMergeExistingGroups: true, out _);

        var allBefore = repo.QuerySlice(StatisticsSlice.All()).Sum(static v => v.Points);
        var request = new PointsTransferRequest(
            PointsTransferSource.ForUnattributed(StreamingPlatform.Twitch),
            YouTubeMain,
            PointsTransferMode.AllCounters);

        var preview = repo.PreviewTransfer(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview.TransferredCount, Is.EqualTo(1));
            Assert.That(preview.SkippedCount, Is.EqualTo(2));
            Assert.That(preview.Users.Single(u => u.SourceUserKey == twLinked).SkipReason, Is.EqualTo(PointsTransferSkipReason.None));
            Assert.That(preview.Users.Single(u => u.SourceUserKey == twUnlinked).SkipReason, Is.EqualTo(PointsTransferSkipReason.MissingTargetAccountLink));
            Assert.That(preview.Users.Single(u => u.SourceUserKey == twAmbiguous).SkipReason, Is.EqualTo(PointsTransferSkipReason.AmbiguousTargetAccountLink));
        }

        var result = repo.ExecuteTransfer(request);
        var allAfter = repo.QuerySlice(StatisticsSlice.All()).Sum(static v => v.Points);
        var targetLinked = repo.FindViewerById(StatisticsSlice.ForChannel(YouTubeMain), ytLinked)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TransferredCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.EqualTo(2));
            Assert.That(result.MissingLinkSkippedCount, Is.EqualTo(1));
            Assert.That(result.AmbiguousLinkSkippedCount, Is.EqualTo(1));
            Assert.That(allAfter, Is.EqualTo(allBefore));
            Assert.That(targetLinked.MessageCount, Is.EqualTo(8UL));
            Assert.That(targetLinked.BonusPoints, Is.EqualTo(5UL));
            Assert.That(repo.QuerySlice(StatisticsSlice.Unattributed(StreamingPlatform.Twitch)), Has.Count.EqualTo(2));
            Assert.That(repo.FindViewerById(StatisticsSlice.Unattributed(StreamingPlatform.Twitch), twLinked), Is.Null);
        }
    }

    [Test]
    public void Same_source_and_target_is_rejected()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var request = new PointsTransferRequest(
            PointsTransferSource.ForChannel(TwitchMain),
            TwitchMain,
            PointsTransferMode.AllCounters);

        Assert.Throws<ArgumentException>(() => repo.PreviewTransfer(request));
        Assert.Throws<ArgumentException>(() => repo.ExecuteTransfer(request));
    }

    [Test]
    public async Task Service_persists_when_loaded_publishes_event_and_respects_revoked_write_permission()
    {
        var graph = new StatisticsTestGraph(_directory);
        await graph.UserLoader.EnsureLoadedAsync();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        UserPointsTransferred? published = null;
        using var sub = bus.Subscribe<UserPointsTransferred>(ev => published = ev);

        var service = new PointsTransferService(graph.UserRepository, bus, graph.AutoSaver, graph.UserLoader);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        graph.UserRepository.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "u1", Name = "Alice", MessageCount = 5, BonusPoints = 3 },
        ]);

        var exec1 = await service.TransferAsync(new(
            PointsTransferSource.ForUnattributed(StreamingPlatform.Twitch),
            TwitchMain,
            PointsTransferMode.AllCounters));

        var catalogPath = Path.Combine(_directory, "user_statistics_catalog.json");
        var persistedBytesAfterFirst = await File.ReadAllTextAsync(catalogPath);

        Assert.That(exec1.PersistedToDisk, Is.True);
        Assert.That(published, Is.Not.Null);
        Assert.That(published!.Result.TransferredCount, Is.EqualTo(1));

        await graph.UserLoader.InvalidateAsync();
        graph.UserRepository.TrackMessage(TwitchMain, new(StreamingPlatform.Twitch, "u2"), "Bob");

        var exec2 = await service.TransferAsync(new(
            PointsTransferSource.ForChannel(TwitchMain),
            TwitchSecond,
            PointsTransferMode.AllCounters));

        var persistedBytesAfterRevoke = await File.ReadAllTextAsync(catalogPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exec2.Result.TransferredCount, Is.EqualTo(2));
            Assert.That(exec2.PersistedToDisk, Is.False);
            Assert.That(persistedBytesAfterRevoke, Is.EqualTo(persistedBytesAfterFirst));
            Assert.That(graph.UserRepository.FindViewerById(StatisticsSlice.ForChannel(TwitchSecond), u1)?.Points, Is.EqualTo(8));
        }
    }

    [Test]
    public void Target_overflow_preserves_untransferred_remainder_in_source()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        repo.ReplaceCatalog(new()
        {
            Accounts = [new() { Platform = StreamingPlatform.Twitch, UserId = "u1", Name = "Alice" }],
            ChannelCounters =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw_main", UserId = "u1", BonusPoints = 5 },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw_second", UserId = "u1", BonusPoints = ulong.MaxValue - 1 },
            ],
        });

        var request = new PointsTransferRequest(
            PointsTransferSource.ForChannel(TwitchMain),
            TwitchSecond,
            PointsTransferMode.AdjustmentsOnly);

        var preview = repo.PreviewTransfer(request);
        var result = repo.ExecuteTransfer(request);

        var sourceAfter = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1);
        var targetAfter = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchSecond), u1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preview.TotalBonusPoints, Is.EqualTo(1UL));
            Assert.That(result.TotalBonusPoints, Is.EqualTo(1UL));
            Assert.That(targetAfter?.BonusPoints, Is.EqualTo(ulong.MaxValue));
            Assert.That(sourceAfter?.BonusPoints, Is.EqualTo(4UL));
        }
    }

    [Test]
    public void Adjustments_only_mode_preserves_existing_target_activity_dates()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        var targetFirst = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);
        var targetLast = new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc);
        var sourceFirst = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var sourceLast = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);

        repo.ReplaceCatalog(new()
        {
            Accounts = [new() { Platform = StreamingPlatform.Twitch, UserId = "u1", Name = "Alice", FirstSeen = targetFirst, LastSeen = targetLast }],
            ChannelCounters =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw_main", UserId = "u1", MessageCount = 5, BonusPoints = 10, FirstSeen = sourceFirst, LastSeen = sourceLast },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "tw_second", UserId = "u1", MessageCount = 2, FirstSeen = targetFirst, LastSeen = targetLast },
            ],
        });

        repo.ExecuteTransfer(new(
            PointsTransferSource.ForChannel(TwitchMain),
            TwitchSecond,
            PointsTransferMode.AdjustmentsOnly));

        var targetAfter = repo.FindViewerById(StatisticsSlice.ForChannel(TwitchSecond), u1)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetAfter.FirstSeen, Is.EqualTo(targetFirst));
            Assert.That(targetAfter.LastSeen, Is.EqualTo(targetLast));
            Assert.That(targetAfter.BonusPoints, Is.EqualTo(10UL));
            Assert.That(targetAfter.MessageCount, Is.EqualTo(2UL));
        }
    }

    [Test]
    public void Canceled_token_does_not_mutate_balances()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var service = new PointsTransferService(repo, bus);
        var u1 = new PlatformUserKey(StreamingPlatform.Twitch, "u1");
        repo.TrackMessage(TwitchMain, u1, "Alice");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.TransferAsync(
                new(PointsTransferSource.ForChannel(TwitchMain), TwitchSecond, PointsTransferMode.AllCounters),
                cts.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchMain), u1)?.MessageCount, Is.EqualTo(1UL));
            Assert.That(repo.FindViewerById(StatisticsSlice.ForChannel(TwitchSecond), u1), Is.Null);
        }
    }
}
