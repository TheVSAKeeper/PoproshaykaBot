using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public sealed class MultiplatformUserCommandsTests
{
    private string _directory = null!;
    private UserStatisticsRepository _repository = null!;
    private UserRankService _rankService = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-cmd-platform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        var settingsManager = new SettingsManager(
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SettingsManager>>(),
            settingsFilePath: Path.Combine(_directory, "settings.json"));

        _repository = new(NullLogger<UserStatisticsRepository>.Instance);
        _rankService = new(settingsManager);

        var twitchUser = UserStatistics.Create(StreamingPlatform.Twitch, "shared-1", "TwitchUser");
        twitchUser.MessageCount = 40;
        twitchUser.BonusPoints = 10;

        var youtubeUser = UserStatistics.Create(StreamingPlatform.YouTube, "shared-1", "YouTubeUser");
        youtubeUser.MessageCount = 3;
        youtubeUser.BonusPoints = 1;

        var otherYoutube = UserStatistics.Create(StreamingPlatform.YouTube, "yt-only", "TubeViewer");
        otherYoutube.MessageCount = 20;

        _repository.SeedPlatform(StreamingPlatform.Twitch, [twitchUser]);
        _repository.SeedPlatform(StreamingPlatform.YouTube, [youtubeUser, otherYoutube]);
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
    public async Task RankAndStatsCommands_QueryRequestedPlatformPartition()
    {
        var rank = new RankCommand(_repository, _rankService);
        var profile = new MyProfileCommand(_repository, _rankService);
        var messages = new HowManyMessagesCommand(_repository);
        var top = new TopUsersCommand(_repository, _rankService);

        var ytSelf = Context(StreamingPlatform.YouTube, "shared-1", "YouTubeUser");
        var twSelf = Context(StreamingPlatform.Twitch, "shared-1", "TwitchUser");
        var ytLookup = Context(StreamingPlatform.YouTube, "viewer", "Caller", "@TubeViewer");
        var twCrossLookup = Context(StreamingPlatform.Twitch, "viewer", "Caller", "@TubeViewer");

        var ytRank = await rank.ExecuteAsync(ytSelf, CancellationToken.None);
        var twRank = await rank.ExecuteAsync(twSelf, CancellationToken.None);
        var ytProfile = await profile.ExecuteAsync(ytSelf, CancellationToken.None);
        var ytMsgs = await messages.ExecuteAsync(ytSelf, CancellationToken.None);
        var ytLookupReply = await rank.ExecuteAsync(ytLookup, CancellationToken.None);
        var twMissReply = await rank.ExecuteAsync(twCrossLookup, CancellationToken.None);
        var ytTop = await top.ExecuteAsync(Context(StreamingPlatform.YouTube, "shared-1", "YouTubeUser"), CancellationToken.None);
        var twTop = await top.ExecuteAsync(Context(StreamingPlatform.Twitch, "shared-1", "TwitchUser"), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ytRank?.Text, Does.Contain(_rankService.FormatPoints(4)));
            Assert.That(ytRank?.Text, Does.Contain("3 сообщ."));
            Assert.That(twRank?.Text, Does.Contain(_rankService.FormatPoints(50)));
            Assert.That(twRank?.Text, Does.Contain("40 сообщ."));
            Assert.That(ytProfile?.Text, Does.Contain("3 мсг"));
            Assert.That(ytMsgs?.Text, Does.Contain("3 сообщений"));
            Assert.That(ytLookupReply?.Text, Does.Contain("TubeViewer"));
            Assert.That(twMissReply?.Text, Does.Contain("не найден"));
            Assert.That(ytTop?.Text, Does.Contain("TubeViewer"));
            Assert.That(ytTop?.Text, Does.Not.Contain("TwitchUser"));
            Assert.That(twTop?.Text, Does.Contain("TwitchUser"));
            Assert.That(twTop?.Text, Does.Not.Contain("TubeViewer"));
        }
    }

    private static CommandContext Context(StreamingPlatform platform, string userId, string userName, params string[] arguments)
    {
        return new()
        {
            Platform = platform,
            Channel = "channel",
            MessageId = "msg-1",
            UserId = userId,
            Username = userName.ToLowerInvariant(),
            DisplayName = userName,
            Arguments = arguments,
        };
    }
}
