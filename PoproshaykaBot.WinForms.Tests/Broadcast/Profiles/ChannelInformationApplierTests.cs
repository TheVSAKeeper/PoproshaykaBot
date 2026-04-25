using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Tests.Broadcast.Profiles;

[TestFixture]
public class ChannelInformationApplierTests
{
    [SetUp]
    public void SetUp()
    {
        _channelsApi = Substitute.For<ITwitchChannelsApi>();
        _idProvider = Substitute.For<IBroadcasterIdProvider>();
        _eventBus = Substitute.For<IEventBus>();
        _idProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("12345");
        _applier = new(_channelsApi, _idProvider, _eventBus,
            NullLogger<ChannelInformationApplier>.Instance);
    }

    private ITwitchChannelsApi _channelsApi = null!;
    private IBroadcasterIdProvider _idProvider = null!;
    private IEventBus _eventBus = null!;
    private ChannelInformationApplier _applier = null!;

    [Test]
    public async Task ApplyAsync_SendsFullRequest_AndPublishesApplied()
    {
        var profile = new BroadcastProfile
        {
            Name = "Just Chatting",
            Title = "Test stream",
            GameId = "509658",
            GameName = "Just Chatting",
            BroadcasterLanguage = "ru",
            Tags = ["Russian", "Chatting"],
        };

        await _applier.ApplyAsync(profile, CancellationToken.None);

        await _channelsApi.Received(1)
            .ModifyChannelInformationAsync("12345",
                Arg.Is<PatchChannelRequest>(r =>
                    r.Title == "Test stream" && r.GameId == "509658" && r.BroadcasterLanguage == "ru" && r.Tags!.SequenceEqual(new[] { "Russian", "Chatting" })),
                Arg.Any<CancellationToken>());

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<BroadcastProfileApplied>(e => e.Profile == profile),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyAsync_EmptyGameId_DoesNotSendGameId()
    {
        var profile = new BroadcastProfile
        {
            Name = "N",
            Title = "T",
            GameId = "",
        };

        await _applier.ApplyAsync(profile, CancellationToken.None);

        await _channelsApi.Received(1)
            .ModifyChannelInformationAsync("12345",
                Arg.Is<PatchChannelRequest>(r => r.GameId == null),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyAsync_NoBroadcasterId_PublishesFailed()
    {
        _idProvider.GetAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        var profile = new BroadcastProfile
        {
            Name = "N",
            Title = "T",
        };

        await _applier.ApplyAsync(profile, CancellationToken.None);

        await _channelsApi.DidNotReceiveWithAnyArgs().ModifyChannelInformationAsync(null!, null!, CancellationToken.None);
        await _eventBus.Received(1)
            .PublishAsync(Arg.Any<BroadcastProfileApplyFailed>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyAsync_ApiThrows_PublishesFailed()
    {
        _channelsApi
            .ModifyChannelInformationAsync(Arg.Any<string>(), Arg.Any<PatchChannelRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var profile = new BroadcastProfile
        {
            Name = "N",
            Title = "T",
        };

        await _applier.ApplyAsync(profile, CancellationToken.None);

        await _eventBus.Received(1)
            .PublishAsync(Arg.Is<BroadcastProfileApplyFailed>(e => !e.ErrorMessage.Contains("boom") && !string.IsNullOrEmpty(e.ErrorMessage)),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyPatchAsync_OnlyTitle_SendsOnlyTitle()
    {
        await _applier.ApplyPatchAsync("Hello", null, null, CancellationToken.None);

        await _channelsApi.Received(1)
            .ModifyChannelInformationAsync("12345",
                Arg.Is<PatchChannelRequest>(r =>
                    r.Title == "Hello" && r.GameId == null && r.BroadcasterLanguage == null && r.Tags == null),
                Arg.Any<CancellationToken>());
    }
}
