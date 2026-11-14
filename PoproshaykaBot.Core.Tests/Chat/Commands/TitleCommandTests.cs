using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public class TitleCommandTests
{
    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IChannelInformationApplier>();
        _command = new(_applier,
            NullLogger<TitleCommand>.Instance);
    }

    private IChannelInformationApplier _applier = null!;
    private TitleCommand _command = null!;

    [Test]
    public void CanExecute_OnlyForBroadcasterOrMod()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_command.CanExecute(new()
            {
                Platform = StreamingPlatform.Twitch,
                IsBroadcaster = true,
            }), Is.True);

            Assert.That(_command.CanExecute(new()
            {
                Platform = StreamingPlatform.Twitch,
                IsModerator = true,
            }), Is.True);

            Assert.That(_command.CanExecute(new()
            {
                Platform = StreamingPlatform.Twitch,
            }), Is.False);
        }
    }

    [Test]
    public async Task Execute_NoArgs_ReturnsUsage()
    {
        var response = await _command.ExecuteAsync(new()
        {
            Platform = StreamingPlatform.Twitch,
            IsBroadcaster = true,
            Arguments = [],
        }, CancellationToken.None);

        Assert.That(response!.Text, Does.Contain("!title"));
    }

    [Test]
    public async Task Execute_WithTitle_ReturnsConfirmation()
    {
        var ctx = new CommandContext
        {
            Platform = StreamingPlatform.Twitch,
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["новое", "название"],
        };

        var response = await _command.ExecuteAsync(ctx, CancellationToken.None);

        Assert.That(response!.Text, Does.Contain("Название обновлено"));
    }
}
