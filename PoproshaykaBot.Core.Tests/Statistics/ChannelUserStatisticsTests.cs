using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class ChannelUserStatisticsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-channel-stats-{Guid.NewGuid():N}");
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
    public void Same_viewer_across_two_channels_keeps_channel_counters_separate_and_aggregates_total_once()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var viewer = new PlatformUserKey(StreamingPlatform.YouTube, "UC_viewer_1");
        var ytA = new ChannelKey(StreamingPlatform.YouTube, "UC_channel_A");
        var ytB = new ChannelKey(StreamingPlatform.YouTube, "UC_channel_B");

        for (var i = 0; i < 5; i++)
        {
            repo.TrackMessage(ytA, viewer, "Alex");
        }

        for (var i = 0; i < 3; i++)
        {
            repo.TrackMessage(ytB, viewer, "Alex");
        }

        var inA = repo.QuerySlice(StatisticsSlice.ForChannel(ytA));
        var inB = repo.QuerySlice(StatisticsSlice.ForChannel(ytB));
        var all = repo.QuerySlice(StatisticsSlice.All());
        var platform = repo.QuerySlice(StatisticsSlice.ForPlatform(StreamingPlatform.YouTube));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repo.GetKnownAccounts(), Has.Count.EqualTo(1));
            Assert.That(inA.Single().MessageCount, Is.EqualTo(5UL));
            Assert.That(inB.Single().MessageCount, Is.EqualTo(3UL));
            Assert.That(all, Has.Count.EqualTo(1));
            Assert.That(all[0].MessageCount, Is.EqualTo(8UL));
            Assert.That(all[0].ChannelBreakdowns, Has.Count.EqualTo(2));
            Assert.That(platform.Single().MessageCount, Is.EqualTo(8UL));
        }
    }

    [Test]
    public void Same_raw_user_id_on_two_platforms_stays_isolated()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var twitchUser = new PlatformUserKey(StreamingPlatform.Twitch, "42");
        var youtubeUser = new PlatformUserKey(StreamingPlatform.YouTube, "42");
        var twitchChannel = new ChannelKey(StreamingPlatform.Twitch, "broadcaster_tw");
        var youtubeChannel = new ChannelKey(StreamingPlatform.YouTube, "UC_broadcaster_yt");

        repo.TrackMessage(twitchChannel, twitchUser, "SameName");
        repo.TrackMessage(twitchChannel, twitchUser, "SameName");
        repo.TrackMessage(youtubeChannel, youtubeUser, "SameName");

        var all = repo.QuerySlice(StatisticsSlice.All());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repo.GetKnownAccounts(), Has.Count.EqualTo(2));
            Assert.That(all, Has.Count.EqualTo(2));
            Assert.That(repo.FindViewerById(StatisticsSlice.All(), twitchUser)!.MessageCount, Is.EqualTo(2UL));
            Assert.That(repo.FindViewerById(StatisticsSlice.All(), youtubeUser)!.MessageCount, Is.EqualTo(1UL));
        }
    }

    [Test]
    public void Viewer_nickname_change_and_channel_rename_do_not_split_counters()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var catalog = new ChannelCatalogStore(null, Path.Combine(_directory, "channels.json"));
        var channel = new ChannelKey(StreamingPlatform.Twitch, "1001");
        var viewer = new PlatformUserKey(StreamingPlatform.Twitch, "777");

        catalog.EnsureChannelKnown(channel, "OldChannelLogin");
        repo.TrackMessage(channel, viewer, "OldNick");
        repo.IncrementBonusPoints(StatisticsCounterTarget.ForChannel(channel), viewer, 25);

        catalog.EnsureChannelKnown(channel, "RenamedChannelLogin");
        repo.TrackMessage(channel, viewer, "NewNick");

        var aggregate = repo.FindViewerById(StatisticsSlice.ForChannel(channel), viewer)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repo.GetKnownChannels(), Has.Count.EqualTo(1));
            Assert.That(catalog.ResolveDisplayName(channel), Is.EqualTo("RenamedChannelLogin"));
            Assert.That(aggregate.DisplayName, Is.EqualTo("NewNick"));
            Assert.That(aggregate.MessageCount, Is.EqualTo(2UL));
            Assert.That(aggregate.BonusPoints, Is.EqualTo(25UL));
            Assert.That(aggregate.Points, Is.EqualTo(27L));
        }
    }

    [Test]
    public void Channel_groups_deduplicate_members_and_All_never_double_counts_overlapping_groups()
    {
        var repo = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var catalog = new ChannelCatalogStore(null, Path.Combine(_directory, "channels.json"));
        var ch1 = new ChannelKey(StreamingPlatform.Twitch, "tw_1");
        var ch2 = new ChannelKey(StreamingPlatform.YouTube, "yt_2");
        var viewer = new PlatformUserKey(StreamingPlatform.Twitch, "u_1");
        var ytViewer = new PlatformUserKey(StreamingPlatform.YouTube, "yu_1");

        repo.TrackMessage(ch1, viewer, "Viewer");
        repo.TrackMessage(ch1, viewer, "Viewer");
        repo.TrackMessage(ch2, ytViewer, "Viewer");

        catalog.Save(new ChannelsSettings
        {
            Groups =
            [
                new ChannelGroupDefinition
                {
                    Id = "g1",
                    Name = "Основные",
                    Channels =
                    [
                        new ChannelGroupMember { Platform = ch1.Platform, ChannelId = ch1.ChannelId },
                        new ChannelGroupMember { Platform = ch1.Platform, ChannelId = ch1.ChannelId },
                    ],
                },
                new ChannelGroupDefinition
                {
                    Id = "g2",
                    Name = "Резерв",
                    Channels =
                    [
                        new ChannelGroupMember { Platform = ch1.Platform, ChannelId = ch1.ChannelId },
                        new ChannelGroupMember { Platform = ch2.Platform, ChannelId = ch2.ChannelId },
                    ],
                },
            ],
        });

        var dedupedG1 = ChannelCatalogStore.DeduplicateMembers(catalog.FindGroup("g1")!.Channels);
        var group1Slice = repo.QuerySlice(StatisticsSlice.ForGroup("g1", "Основные", dedupedG1));
        var group2Slice = repo.QuerySlice(StatisticsSlice.ForGroup("g2", "Резерв", [ch1, ch2]));
        var allSlice = repo.QuerySlice(StatisticsSlice.All());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dedupedG1, Has.Count.EqualTo(1));
            Assert.That(group1Slice.Single().MessageCount, Is.EqualTo(2UL));
            Assert.That(group2Slice.Sum(static v => (long)v.MessageCount), Is.EqualTo(3L));
            Assert.That(allSlice.Sum(static v => (long)v.MessageCount), Is.EqualTo(3L));
        }
    }

    [Test]
    public async Task Catalog_round_trip_preserves_accounts_counters_unattributed_and_links()
    {
        var graph1 = new StatisticsTestGraph(_directory);
        await graph1.UserLoader.EnsureLoadedAsync();
        var twitchCh = new ChannelKey(StreamingPlatform.Twitch, "tw_ch");
        var youtubeCh = new ChannelKey(StreamingPlatform.YouTube, "yt_ch");
        var twUser = new PlatformUserKey(StreamingPlatform.Twitch, "tw_1");
        var ytUser = new PlatformUserKey(StreamingPlatform.YouTube, "yt_1");

        graph1.UserRepository.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "tw_1", Name = "TwUser", MessageCount = 4, BonusPoints = 10 },
        ]);
        graph1.UserRepository.TrackMessage(twitchCh, twUser, "TwUser");
        graph1.UserRepository.TrackMessage(youtubeCh, ytUser, "YtUser");

        var linkedStatus = graph1.UserRepository.LinkAccounts(
            [twUser, ytUser],
            AccountLinkOrigin.ViewerConfirmed,
            new(AccountLinkAuthorKind.Viewer, StreamingPlatform.Twitch, "tw_1", "TwUser"),
            allowMergeExistingGroups: false,
            out _);

        Assert.That(linkedStatus, Is.EqualTo(AccountLinkOperationStatus.Linked));

        await graph1.AutoSaver.SaveNowAsync();

        var graph2 = new StatisticsTestGraph(_directory);
        await graph2.UserLoader.EnsureLoadedAsync();
        var loaded = graph2.UserRepository.QuerySlice(StatisticsSlice.All());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_directory, "user_statistics_catalog.json")), Is.True);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].IsLinked, Is.True);
            Assert.That(loaded[0].MessageCount, Is.EqualTo(6UL));
            Assert.That(loaded[0].BonusPoints, Is.EqualTo(10UL));
            Assert.That(loaded[0].ChannelBreakdowns.Select(static b => b.Target), Does.Contain(StatisticsCounterTarget.ForUnattributed(StreamingPlatform.Twitch)));
            Assert.That(loaded[0].ChannelBreakdowns.Select(static b => b.Target), Does.Contain(StatisticsCounterTarget.ForChannel(twitchCh)));
            Assert.That(loaded[0].ChannelBreakdowns.Select(static b => b.Target), Does.Contain(StatisticsCounterTarget.ForChannel(youtubeCh)));
        }
    }
}
