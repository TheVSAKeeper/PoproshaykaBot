using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Tests.Server;

[TestFixture]
public sealed class OverlayUserRankResolverTests
{
    private string _tempDir = null!;
    private SettingsManager _settingsManager = null!;
    private UserStatisticsRepository _repo = null!;
    private UserRankService _rankService = null!;
    private OverlayUserRankResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "poproshayka-rank-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settingsManager = new(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        _repo = new(NullLogger<UserStatisticsRepository>.Instance);
        _rankService = new(_settingsManager);
        _resolver = new(_repo, _rankService);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Test]
    public void Resolve_ReturnsMatchingRank_ForTwitchAndYouTubeChannelUsers()
    {
        var twitchChannel = new ChannelKey(StreamingPlatform.Twitch, "tw-1");
        var youtubeChannel = new ChannelKey(StreamingPlatform.YouTube, "UC-yt-1");
        var twitchUser = new PlatformUserKey(StreamingPlatform.Twitch, "u-tw");
        var youtubeUser = new PlatformUserKey(StreamingPlatform.YouTube, "u-yt");

        for (var i = 0; i < 60; i++)
        {
            _repo.TrackMessage(twitchChannel, twitchUser, "twitcher");
        }

        for (var i = 0; i < 200; i++)
        {
            _repo.TrackMessage(youtubeChannel, youtubeUser, "youtuber");
        }

        var twRank = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-1",
            UserId = "u-tw",
            MessageType = ChatMessageType.UserMessage,
        });

        var ytRank = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.YouTube,
            ChannelId = "UC-yt-1",
            UserId = "u-yt",
            MessageType = ChatMessageType.UserMessage,
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(twRank, Is.EqualTo(_rankService.GetRank(60)));
            Assert.That(ytRank, Is.EqualTo(_rankService.GetRank(200)));
        }
    }

    [Test]
    public void Resolve_ReturnsNull_ForBotSystemOrEmptyUserId()
    {
        var bot = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-1",
            UserId = "bot-1",
            IsBot = true,
            MessageType = ChatMessageType.UserMessage,
        });

        var system = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-1",
            UserId = "u-1",
            MessageType = ChatMessageType.SystemNotification,
        });

        var emptyUser = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-1",
            UserId = "   ",
            MessageType = ChatMessageType.UserMessage,
        });

        var withoutDeps = new OverlayUserRankResolver().Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = "tw-1",
            UserId = "u-1",
            MessageType = ChatMessageType.UserMessage,
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bot, Is.Null);
            Assert.That(system, Is.Null);
            Assert.That(emptyUser, Is.Null);
            Assert.That(withoutDeps, Is.Null);
        }
    }

    [Test]
    public void Resolve_UnknownViewer_ReturnsZeroPointsRank_AndUsesPlatformSliceWhenChannelIdEmpty()
    {
        var unknown = _resolver.Resolve(new()
        {
            Platform = StreamingPlatform.Twitch,
            ChannelId = string.Empty,
            UserId = "stranger",
            MessageType = ChatMessageType.UserMessage,
        });

        Assert.That(unknown, Is.EqualTo(_rankService.GetRank(0)));
    }
}
