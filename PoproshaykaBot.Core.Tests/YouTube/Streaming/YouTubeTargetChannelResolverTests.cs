using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Streaming;

[TestFixture]
public class YouTubeTargetChannelResolverTests
{
    private string _directory = null!;
    private YouTubeAccountsStore _accountsStore = null!;
    private IYouTubeLiveBroadcastClient _client = null!;
    private YouTubeTargetChannelResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-channel-res-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _accountsStore = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_directory, "youtube-accounts.json"));
        _accountsStore.SaveAll(
            "secret",
            new YouTubeAccountSettings(),
            new YouTubeAccountSettings { ChannelId = "UC0000000000000000000099", ChannelTitle = "Broadcaster" });

        _client = Substitute.For<IYouTubeLiveBroadcastClient>();
        _resolver = new YouTubeTargetChannelResolver(_client, _accountsStore, NullLogger<YouTubeTargetChannelResolver>.Instance);
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
    public async Task Blank_And_Direct_Channel_Inputs_Use_Zero_Api_Calls()
    {
        var fromBlank = await _resolver.ResolveAsync(new YouTubeSettings { Channel = string.Empty }, CancellationToken.None);
        var fromId = await _resolver.ResolveAsync(new YouTubeSettings { Channel = "UC1234567890123456789012" }, CancellationToken.None);
        var fromUrl = await _resolver.ResolveAsync(new YouTubeSettings { Channel = "https://www.youtube.com/channel/UC1234567890123456789012?sub=1" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(fromBlank, Is.EqualTo("UC0000000000000000000099"));
            Assert.That(fromId, Is.EqualTo("UC1234567890123456789012"));
            Assert.That(fromUrl, Is.EqualTo("UC1234567890123456789012"));
        });

        await _client.DidNotReceiveWithAnyArgs().ResolveChannelIdByHandleAsync(default!, default);
    }

    [Test]
    public async Task Handle_And_Handle_Url_Are_Normalized_And_Cached_Per_Project()
    {
        _client.ResolveChannelIdByHandleAsync("@streamer", Arg.Any<CancellationToken>())
            .Returns("UCabcdef1234567890123456");

        var settings = new YouTubeSettings { ProjectId = "proj-a", Channel = "https://youtube.com/@streamer/live" };
        var first = await _resolver.ResolveAsync(settings, CancellationToken.None);
        var second = await _resolver.ResolveAsync(new YouTubeSettings { ProjectId = "proj-a", Channel = "streamer" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("UCabcdef1234567890123456"));
            Assert.That(second, Is.EqualTo("UCabcdef1234567890123456"));
        });

        await _client.Received(1).ResolveChannelIdByHandleAsync("@streamer", Arg.Any<CancellationToken>());
    }
}
