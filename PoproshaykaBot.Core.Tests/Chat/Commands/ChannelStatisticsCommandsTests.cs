using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Statistics.Linking;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public sealed class ChannelStatisticsCommandsTests
{
    private string _directory = null!;
    private UserStatisticsRepository _repo = null!;
    private ChannelCatalogStore _catalog = null!;
    private UserRankService _ranks = null!;
    private AccountLinkingService _linking = null!;

    private static readonly ChannelKey TwitchA = new(StreamingPlatform.Twitch, "1001");
    private static readonly ChannelKey YouTubeA = new(StreamingPlatform.YouTube, "UC_yt_a");
    private static readonly ChannelKey YouTubeB = new(StreamingPlatform.YouTube, "UC_yt_b");
    private static readonly PlatformUserKey AliceTw = new(StreamingPlatform.Twitch, "tw_alice");
    private static readonly PlatformUserKey AliceYt = new(StreamingPlatform.YouTube, "yt_alice");
    private static readonly PlatformUserKey BobYt = new(StreamingPlatform.YouTube, "yt_bob");

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-cmd-slices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        _repo = new(NullLogger<UserStatisticsRepository>.Instance);
        _catalog = new(null, Path.Combine(_directory, "channels.json"));
        _ranks = new(settingsManager);
        _linking = new(_repo, eventBus);

        _catalog.Save(new ChannelsSettings
        {
            Channels =
            [
                new() { Platform = TwitchA.Platform, ChannelId = TwitchA.ChannelId, DisplayName = "tw_main" },
                new() { Platform = YouTubeA.Platform, ChannelId = YouTubeA.ChannelId, DisplayName = "YouTube A" },
                new() { Platform = YouTubeB.Platform, ChannelId = YouTubeB.ChannelId, DisplayName = "YouTube B" },
            ],
            Groups =
            [
                new()
                {
                    Id = "main",
                    Name = "Основные",
                    Channels =
                    [
                        new() { Platform = TwitchA.Platform, ChannelId = TwitchA.ChannelId },
                        new() { Platform = YouTubeA.Platform, ChannelId = YouTubeA.ChannelId },
                    ],
                },
            ],
        });

        for (var i = 0; i < 4; i++)
        {
            _repo.TrackMessage(TwitchA, AliceTw, "Alice");
        }

        for (var i = 0; i < 6; i++)
        {
            _repo.TrackMessage(YouTubeA, AliceYt, "AliceYT");
        }

        for (var i = 0; i < 3; i++)
        {
            _repo.TrackMessage(YouTubeB, AliceYt, "AliceYT");
        }

        for (var i = 0; i < 5; i++)
        {
            _repo.TrackMessage(YouTubeA, BobYt, "BobYT");
        }
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
    public async Task Rank_profile_messages_and_top_default_to_caller_channel_and_honor_group_and_all_with_links()
    {
        await _linking.LinkManualAsync(
            [AliceTw, AliceYt],
            new(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer"));

        var rank = new RankCommand(_repo, _ranks, _catalog);
        var profile = new MyProfileCommand(_repo, _ranks, _catalog);
        var msgs = new HowManyMessagesCommand(_repo, _catalog);
        var top = new TopUsersCommand(_repo, _ranks, _catalog);

        var ytAContext = Context(YouTubeA, AliceYt, "AliceYT");
        var ytAGroupContext = Context(YouTubeA, AliceYt, "AliceYT", "группа", "Основные");
        var ytAAllContext = Context(YouTubeA, AliceYt, "AliceYT", "все");
        var ytAUnknownGroup = Context(YouTubeA, AliceYt, "AliceYT", "группа", "Несуществующая");

        var rankChannel = await rank.ExecuteAsync(ytAContext, CancellationToken.None);
        var rankGroup = await rank.ExecuteAsync(ytAGroupContext, CancellationToken.None);
        var rankAll = await rank.ExecuteAsync(ytAAllContext, CancellationToken.None);
        var profileGroup = await profile.ExecuteAsync(ytAGroupContext, CancellationToken.None);
        var msgsAll = await msgs.ExecuteAsync(ytAAllContext, CancellationToken.None);
        var topChannel = await top.ExecuteAsync(Context(YouTubeA, BobYt, "BobYT"), CancellationToken.None);
        var topUnknown = await top.ExecuteAsync(ytAUnknownGroup, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rankChannel?.Text, Does.Contain("6 сообщ."));
            Assert.That(rankGroup?.Text, Does.Contain("10 сообщ."));
            Assert.That(rankAll?.Text, Does.Contain("13 сообщ."));
            Assert.That(profileGroup?.Text, Does.Contain("10 мсг"));
            Assert.That(msgsAll?.Text, Does.Contain("13 сообщений"));
            Assert.That(topChannel?.Text, Does.Contain("6").And.Contain("BobYT"));
            Assert.That(topUnknown?.Text, Does.Contain("Несуществующая").And.Contain("Основные"));
        }
    }

    [Test]
    public async Task Link_and_unlink_commands_work_end_to_end_via_chat()
    {
        var linkCmd = new LinkAccountCommand(_linking, _repo);
        var unlinkCmd = new UnlinkAccountCommand(_linking, _repo);

        var step1 = await linkCmd.ExecuteAsync(Context(TwitchA, AliceTw, "Alice"), CancellationToken.None);
        var rCode = step1!.Text.Split(' ')[^1];

        var step2 = await linkCmd.ExecuteAsync(Context(YouTubeA, AliceYt, "AliceYT", rCode), CancellationToken.None);
        var cCode = step2!.Text.Split(' ')[^1];

        var step3 = await linkCmd.ExecuteAsync(Context(TwitchA, AliceTw, "Alice", cCode), CancellationToken.None);

        Assert.That(step3?.Text, Does.Contain("связаны"));
        Assert.That(_repo.GetLinkedKeys(AliceTw), Is.EquivalentTo(new[] { AliceTw, AliceYt }));

        var unlinked = await unlinkCmd.ExecuteAsync(Context(YouTubeA, AliceYt, "AliceYT"), CancellationToken.None);

        Assert.That(unlinked?.Text, Does.Contain("отвязан"));
        Assert.That(_repo.GetLinkedKeys(AliceTw), Is.EquivalentTo(new[] { AliceTw }));
    }

    [Test]
    public async Task WasThere_counts_linked_accounts_across_platforms_without_double_counting_multistream()
    {
        await _linking.LinkManualAsync(
            [AliceTw, AliceYt],
            new(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer"));

        var historyStore = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "stream_sessions.json"));

        var start = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start,
            EndedAt = start.AddHours(2),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = TwitchA.Platform,
                    Channel = "tw_main",
                    ChannelId = TwitchA.ChannelId,
                    StartedAt = start,
                    EndedAt = start.AddHours(2),
                    Chatters = [new StreamSessionChatter { UserId = AliceTw.UserId, DisplayName = "Alice", MessageCount = 3 }],
                },
                new BroadcastPlatformRecord
                {
                    Platform = YouTubeA.Platform,
                    Channel = "YouTube A",
                    ChannelId = YouTubeA.ChannelId,
                    StartedAt = start,
                    EndedAt = start.AddHours(2),
                    Chatters = [new StreamSessionChatter { UserId = AliceYt.UserId, DisplayName = "AliceYT", MessageCount = 2 }],
                },
            ],
        });
        historyStore.TryAppend(new BroadcastRecord
        {
            Id = Guid.NewGuid(),
            StartedAt = start.AddDays(1),
            EndedAt = start.AddDays(1).AddHours(1),
            Platforms =
            [
                new BroadcastPlatformRecord
                {
                    Platform = TwitchA.Platform,
                    Channel = "tw_main",
                    ChannelId = TwitchA.ChannelId,
                    StartedAt = start.AddDays(1),
                    EndedAt = start.AddDays(1).AddHours(1),
                    Chatters = [new StreamSessionChatter { UserId = AliceTw.UserId, DisplayName = "Alice", MessageCount = 1 }],
                },
            ],
        });

        var wasThere = new WasThereCommand(historyStore, _repo);
        var reply = await wasThere.ExecuteAsync(Context(YouTubeA, AliceYt, "AliceYT"), CancellationToken.None);

        Assert.That(reply?.Text, Does.Contain("на 2 стримах из 2"));
    }

    [Test]
    public async Task Link_and_unlink_commands_refuse_foreign_debug_sessions_and_unlink_exact_member_by_nickname()
    {
        await _linking.LinkManualAsync(
            [AliceTw, AliceYt],
            new(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer"));

        var linkCmd = new LinkAccountCommand(_linking, _repo);
        var unlinkCmd = new UnlinkAccountCommand(_linking, _repo);

        var foreignDebugCtx = new CommandContext
        {
            Platform = TwitchA.Platform,
            Channel = "other_streamer",
            ChannelKey = TwitchA,
            MessageId = Guid.NewGuid().ToString("N"),
            UserId = "broadcaster_other",
            Username = "other",
            DisplayName = "Other",
            IsBroadcaster = true,
            RecordsUserData = false,
            Arguments = ["AliceYT"],
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(linkCmd.CanExecute(foreignDebugCtx), Is.False);
            Assert.That(unlinkCmd.CanExecute(foreignDebugCtx), Is.False);
            Assert.That(await unlinkCmd.ExecuteAsync(foreignDebugCtx, CancellationToken.None), Is.Null);
            Assert.That(_repo.GetLinkedKeys(AliceTw), Is.EquivalentTo(new[] { AliceTw, AliceYt }));
        }

        // BobYt added so AliceTw has a linked pair of 3 accounts, then remove exact AltNick "AliceYT"
        await _linking.LinkManualAsync(
            [AliceTw, BobYt],
            new(AccountLinkAuthorKind.Streamer, StreamingPlatform.Twitch, "broadcaster", "Streamer"));

        var streamerUnlinkCtx = new CommandContext
        {
            Platform = TwitchA.Platform,
            Channel = "tw_main",
            ChannelKey = TwitchA,
            MessageId = Guid.NewGuid().ToString("N"),
            UserId = "broadcaster",
            Username = "streamer",
            DisplayName = "Streamer",
            IsBroadcaster = true,
            Arguments = ["AliceYT"],
        };

        var result = await unlinkCmd.ExecuteAsync(streamerUnlinkCtx, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Text, Does.Contain(AliceYt.ToString()));
            Assert.That(_repo.GetLinkedKeys(AliceTw), Is.EquivalentTo(new[] { AliceTw, BobYt }));
            Assert.That(_repo.GetLinkedKeys(AliceYt), Is.EquivalentTo(new[] { AliceYt }));
        }
    }

    private static CommandContext Context(ChannelKey channel, PlatformUserKey user, string displayName, params string[] args)
    {
        return new()
        {
            Platform = channel.Platform,
            Channel = channel.ChannelId,
            ChannelKey = channel,
            MessageId = Guid.NewGuid().ToString("N"),
            UserId = user.UserId,
            Username = displayName.ToLowerInvariant(),
            DisplayName = displayName,
            Arguments = args,
        };
    }
}
