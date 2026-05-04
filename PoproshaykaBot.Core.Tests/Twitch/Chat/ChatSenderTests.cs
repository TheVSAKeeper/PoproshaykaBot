using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Twitch.Chat;

[TestFixture]
public class ChatSenderTests
{
    [SetUp]
    public void SetUp()
    {
        _helix = Substitute.For<ITwitchHelixClient>();
        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _botUserIdProvider = Substitute.For<IBotUserIdProvider>();

        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("1");
        _botUserIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("2");

        _sender = new(_helix, _broadcasterIdProvider, _botUserIdProvider, NullLogger<ChatSender>.Instance);
    }

    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private IBotUserIdProvider _botUserIdProvider = null!;
    private ChatSender _sender = null!;

    [Test]
    public async Task EnqueueAsync_AfterRestart_DeliversMessage()
    {
        var sent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.SendChatMessageAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.TrySetResult(call.ArgAt<string>(2));
                return Task.CompletedTask;
            });

        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.StopAsync(progress, CancellationToken.None);

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync("hello", null, CancellationToken.None);

        var delivered = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await _sender.StopAsync(progress, CancellationToken.None);

        Assert.That(delivered, Is.EqualTo("hello"));
    }
}
